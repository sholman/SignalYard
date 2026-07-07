using Azure;
using Azure.Data.Tables;
using FluentAssertions;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using Moq;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for the dashboard aggregation in <see cref="LogStorageService.GetLogStatsAsync"/>.
/// A mocked TableClient returns a fixed set of entries so counts can be asserted deterministically.
/// </summary>
public class LogStatsTests
{
    // A window fully inside a single month so only one partition is queried (the mock returns the
    // same page for every partition query).
    private static readonly DateTimeOffset To = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = To.AddHours(-24);

    private static LogStorageService CreateService(IReadOnlyList<LogEntry> entries)
    {
        var page = Page<LogEntry>.FromValues(entries.ToList(), null, Mock.Of<Response>());
        var pageable = AsyncPageable<LogEntry>.FromPages(new[] { page });

        var mockLogsTable = new Mock<TableClient>();
        mockLogsTable
            .Setup(x => x.QueryAsync<LogEntry>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var mockServiceClient = new Mock<TableServiceClient>();
        mockServiceClient.Setup(x => x.GetTableClient("Logs")).Returns(mockLogsTable.Object);

        return new LogStorageService(mockServiceClient.Object);
    }

    private static LogEntry Entry(string level, DateTimeOffset timestamp, string app = "App1") => new()
    {
        PartitionKey = LogEntry.CreatePartitionKey(app, timestamp),
        RowKey = LogEntry.CreateRowKey(timestamp),
        Application = app,
        Level = level,
        LogTimestamp = timestamp
    };

    [Fact]
    public async Task GetLogStatsAsync_CountsByLevel_ClassifiesErrorsAndWarnings()
    {
        var entries = new[]
        {
            Entry("Information", To.AddHours(-2)),
            Entry("Information", To.AddHours(-3)),
            Entry("Information", To.AddHours(-4)),
            Entry("Warning", To.AddHours(-5)),
            Entry("Error", To.AddHours(-6)),
            Entry("Error", To.AddHours(-7)),
            Entry("Fatal", To.AddHours(-8)),
        };
        var service = CreateService(entries);

        var stats = await service.GetLogStatsAsync(
            new LogStatsRequest { From = From, To = To, BucketMinutes = 60 },
            allApplicationNames: new[] { "App1" });

        stats.TotalCount.Should().Be(7);
        stats.ErrorCount.Should().Be(3); // Error + Fatal
        stats.WarningCount.Should().Be(1);
        stats.InformationCount.Should().Be(3);
        stats.IsApproximate.Should().BeFalse();
    }

    [Fact]
    public async Task GetLogStatsAsync_BucketsSumToTotals()
    {
        var entries = new[]
        {
            Entry("Information", To.AddHours(-2)),
            Entry("Warning", To.AddHours(-2)),
            Entry("Error", To.AddHours(-10)),
            Entry("Fatal", To.AddHours(-20)),
        };
        var service = CreateService(entries);

        var stats = await service.GetLogStatsAsync(
            new LogStatsRequest { From = From, To = To, BucketMinutes = 60 },
            allApplicationNames: new[] { "App1" });

        stats.Buckets.Should().NotBeEmpty();
        stats.Buckets.Sum(b => b.Total).Should().Be(4);
        stats.Buckets.Sum(b => b.Errors).Should().Be(2);
        stats.Buckets.Sum(b => b.Warnings).Should().Be(1);
        // Every bucket's Others is the non-error, non-warning remainder.
        stats.Buckets.Sum(b => b.Others).Should().Be(1);
    }

    [Fact]
    public async Task GetLogStatsAsync_BuildsPerApplicationBreakdown()
    {
        var entries = new[]
        {
            Entry("Information", To.AddHours(-2)),
            Entry("Error", To.AddHours(-3)),
            Entry("Warning", To.AddHours(-4)),
        };
        var service = CreateService(entries);

        var stats = await service.GetLogStatsAsync(
            new LogStatsRequest { From = From, To = To, BucketMinutes = 60 },
            allApplicationNames: new[] { "App1" });

        stats.Applications.Should().ContainSingle();
        var app = stats.Applications[0];
        app.Application.Should().Be("App1");
        app.Total.Should().Be(3);
        app.Errors.Should().Be(1);
        app.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task GetLogStatsAsync_NoApplications_ReturnsEmptyStatsWithBuckets()
    {
        var service = CreateService(Array.Empty<LogEntry>());

        var stats = await service.GetLogStatsAsync(
            new LogStatsRequest { From = From, To = To, BucketMinutes = 60 },
            allApplicationNames: Array.Empty<string>());

        stats.TotalCount.Should().Be(0);
        stats.Buckets.Should().NotBeEmpty(); // buckets are always laid out for the range
        stats.Applications.Should().BeEmpty();
    }
}
