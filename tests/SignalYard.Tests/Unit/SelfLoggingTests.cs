using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Logging;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for the built-in self-logging feature (in-process ILoggerProvider → bounded queue →
/// background flush into the "signalyard" application) and the collision-aware seeding of that app.
/// </summary>
public class SelfLoggingOptionsTests
{
    [Fact]
    public void Defaults_ShouldBeEnabledWarningFourteenDays()
    {
        var options = new SelfLoggingOptions();

        options.Enabled.Should().BeTrue();
        options.MinimumLevel.Should().Be(LogLevel.Warning);
        options.RetentionDays.Should().Be(14);
        options.ApplicationName.Should().Be("signalyard");
    }
}

public class SignalYardLoggerTests
{
    [Theory]
    [InlineData(LogLevel.Trace, "Verbose")]
    [InlineData(LogLevel.Debug, "Debug")]
    [InlineData(LogLevel.Information, "Information")]
    [InlineData(LogLevel.Warning, "Warning")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Fatal")]
    public void MapLevel_ShouldUseSerilogLevelNames(LogLevel level, string expected)
    {
        SignalYardLogger.MapLevel(level).Should().Be(expected);
    }

    [Fact]
    public void Log_ShouldEnqueueClefEventWithSourceContextAndInstance()
    {
        var queue = new SelfLogQueue(new SelfLoggingOptions());
        var logger = new SignalYardLogger("MyApp.MyClass", queue, "prod-1");

        logger.LogWarning("Disk at {Percent}%", 92);

        var events = queue.DrainRemaining();
        events.Should().HaveCount(1);

        var evt = events[0];
        evt.Level.Should().Be("Warning");
        evt.Message.Should().Be("Disk at 92%");
        evt.Timestamp.Should().NotBeNull();
        evt.Properties.Should().ContainKey("SourceContext").WhoseValue.Should().Be("MyApp.MyClass");
        evt.Properties.Should().ContainKey("Instance").WhoseValue.Should().Be("prod-1");
    }

    [Fact]
    public void Log_ShouldCaptureExceptionAndOmitInstanceWhenNotConfigured()
    {
        var queue = new SelfLogQueue(new SelfLoggingOptions());
        var logger = new SignalYardLogger("MyApp.MyClass", queue, instanceName: null);

        logger.LogError(new InvalidOperationException("boom"), "It broke");

        var evt = queue.DrainRemaining().Should().ContainSingle().Subject;
        evt.Level.Should().Be("Error");
        evt.Exception.Should().Contain("InvalidOperationException").And.Contain("boom");
        evt.Properties.Should().NotContainKey("Instance");
    }
}

public class SelfLogQueueTests
{
    [Fact]
    public void TryEnqueue_ShouldDropAndCountWhenFull()
    {
        var queue = new SelfLogQueue(new SelfLoggingOptions { QueueCapacity = 2 });

        queue.TryEnqueue(new ClefLogEvent { Message = "1" }).Should().BeTrue();
        queue.TryEnqueue(new ClefLogEvent { Message = "2" }).Should().BeTrue();
        // Third exceeds capacity → dropped, not blocked.
        queue.TryEnqueue(new ClefLogEvent { Message = "3" }).Should().BeFalse();

        queue.DroppedCount.Should().Be(1);
        queue.DrainRemaining().Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadBatchAsync_ShouldReturnAvailableEventsUpToMax()
    {
        var queue = new SelfLogQueue(new SelfLoggingOptions());
        queue.TryEnqueue(new ClefLogEvent { Message = "1" });
        queue.TryEnqueue(new ClefLogEvent { Message = "2" });
        queue.TryEnqueue(new ClefLogEvent { Message = "3" });

        var batch = await queue.ReadBatchAsync(maxBatch: 2, CancellationToken.None);

        batch.Should().HaveCount(2);
    }
}

public class SelfLogFlushServiceTests
{
    [Fact]
    public async Task FlushOnce_ShouldWriteQueuedEventsToStorageUnderTheConfiguredApplication()
    {
        var options = new SelfLoggingOptions { ApplicationName = "signalyard", BatchSize = 100 };
        var queue = new SelfLogQueue(options);
        queue.TryEnqueue(new ClefLogEvent { Message = "one", Level = "Warning", Timestamp = DateTimeOffset.UtcNow });
        queue.TryEnqueue(new ClefLogEvent { Message = "two", Level = "Error", Timestamp = DateTimeOffset.UtcNow });

        var mockLogsTable = new Mock<TableClient>();
        mockLogsTable
            .Setup(x => x.SubmitTransactionAsync(
                It.IsAny<IEnumerable<TableTransactionAction>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<IReadOnlyList<Response>>>());

        var mockServiceClient = new Mock<TableServiceClient>();
        mockServiceClient.Setup(x => x.GetTableClient("Logs")).Returns(mockLogsTable.Object);

        var logStorage = new LogStorageService(mockServiceClient.Object);
        var service = new SelfLogFlushService(queue, logStorage, options, NullLogger<SelfLogFlushService>.Instance);

        var written = await service.FlushOnceAsync(CancellationToken.None);

        written.Should().Be(2);
        mockLogsTable.Verify(
            x => x.SubmitTransactionAsync(It.IsAny<IEnumerable<TableTransactionAction>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class SystemApplicationSeedingTests
{
    private static ApplicationStorageService CreateService(Mock<TableClient> applicationsTable)
    {
        var mockServiceClient = new Mock<TableServiceClient>();
        mockServiceClient.Setup(x => x.GetTableClient("Applications")).Returns(applicationsTable.Object);
        mockServiceClient.Setup(x => x.GetTableClient("ApiKeys")).Returns(Mock.Of<TableClient>());

        var apiKeyService = new ApiKeyService(mockServiceClient.Object);
        return new ApplicationStorageService(mockServiceClient.Object, apiKeyService);
    }

    [Fact]
    public async Task EnsureSystemApplication_ShouldCreateWhenAbsent()
    {
        var appsTable = new Mock<TableClient>();
        appsTable
            .Setup(x => x.GetEntityAsync<Application>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));
        appsTable
            .Setup(x => x.AddEntityAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var service = CreateService(appsTable);

        var result = await service.EnsureSystemApplicationAsync("signalyard", 14);

        result.Should().Be(SystemApplicationSeedResult.Created);
        appsTable.Verify(x => x.AddEntityAsync(
            It.Is<Application>(a => a.IsSystem && a.Name == "signalyard" && a.RetentionDays == 14 && a.ApiKeyHash == string.Empty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSystemApplication_ShouldAdoptExistingUserAppPreservingItsKey()
    {
        var existing = new Application
        {
            Name = "signalyard",
            IsSystem = false,
            ApiKeyHash = "existing-hash",
            ApiKeyPrefix = "sy_existing",
            RetentionDays = 365
        };

        var appsTable = new Mock<TableClient>();
        appsTable
            .Setup(x => x.GetEntityAsync<Application>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existing, Mock.Of<Response>()));
        appsTable
            .Setup(x => x.UpdateEntityAsync(
                It.IsAny<Application>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var service = CreateService(appsTable);

        var result = await service.EnsureSystemApplicationAsync("signalyard", 14);

        result.Should().Be(SystemApplicationSeedResult.Adopted);
        appsTable.Verify(x => x.UpdateEntityAsync(
            It.Is<Application>(a => a.IsSystem && a.ApiKeyHash == "existing-hash" && a.RetentionDays == 365),
            It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()), Times.Once);
        appsTable.Verify(x => x.AddEntityAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureSystemApplication_ShouldNoOpWhenAlreadySystem()
    {
        var existing = new Application { Name = "signalyard", IsSystem = true, RetentionDays = 30 };

        var appsTable = new Mock<TableClient>();
        appsTable
            .Setup(x => x.GetEntityAsync<Application>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existing, Mock.Of<Response>()));

        var service = CreateService(appsTable);

        var result = await service.EnsureSystemApplicationAsync("signalyard", 14);

        result.Should().Be(SystemApplicationSeedResult.AlreadyPresent);
        appsTable.Verify(x => x.UpdateEntityAsync(
            It.IsAny<Application>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()), Times.Never);
        appsTable.Verify(x => x.AddEntityAsync(It.IsAny<Application>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
