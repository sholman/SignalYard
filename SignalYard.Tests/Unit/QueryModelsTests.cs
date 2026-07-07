using FluentAssertions;
using SignalYard.Core.Models;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for LogQueryRequest and related models
/// </summary>
public class QueryModelsTests
{
    [Fact]
    public void LogQueryRequest_ShouldHaveDefaultMaxResults()
    {
        // Arrange & Act
        var request = new LogQueryRequest
        {
            From = DateTimeOffset.Now.AddDays(-1),
            To = DateTimeOffset.Now
        };

        // Assert
        request.MaxResults.Should().Be(1000);
    }

    [Fact]
    public void LogQueryRequest_ShouldAllowOptionalFilters()
    {
        // Arrange & Act
        var request = new LogQueryRequest
        {
            From = DateTimeOffset.Now.AddDays(-1),
            To = DateTimeOffset.Now,
            Application = null,
            Level = null
        };

        // Assert
        request.Application.Should().BeNull();
        request.Level.Should().BeNull();
    }

    [Fact]
    public void LogQueryRequest_ShouldAcceptAllFilters()
    {
        // Arrange
        var from = DateTimeOffset.Now.AddDays(-7);
        var to = DateTimeOffset.Now;

        // Act
        var request = new LogQueryRequest
        {
            From = from,
            To = to,
            Application = "MyApp",
            Level = "Error",
            MaxResults = 500
        };

        // Assert
        request.From.Should().Be(from);
        request.To.Should().Be(to);
        request.Application.Should().Be("MyApp");
        request.Level.Should().Be("Error");
        request.MaxResults.Should().Be(500);
    }

    [Fact]
    public void LogQueryResult_ShouldStoreAllProperties()
    {
        // Arrange
        var timestamp = DateTimeOffset.Now;

        // Act
        var result = new LogQueryResult
        {
            Id = "test-id-123",
            Timestamp = timestamp,
            Application = "TestApp",
            Level = "Warning",
            Message = "Test warning message",
            MessageTemplate = "Test {Type} message",
            Exception = "System.Exception: Test",
            EventId = "EVT001",
            Properties = new Dictionary<string, object> { { "Type", "warning" } }
        };

        // Assert
        result.Id.Should().Be("test-id-123");
        result.Timestamp.Should().Be(timestamp);
        result.Application.Should().Be("TestApp");
        result.Level.Should().Be("Warning");
        result.Message.Should().Be("Test warning message");
        result.MessageTemplate.Should().Be("Test {Type} message");
        result.Exception.Should().Be("System.Exception: Test");
        result.EventId.Should().Be("EVT001");
        result.Properties.Should().ContainKey("Type");
    }

    [Fact]
    public void LogQueryResponse_ShouldInitializeWithEmptyLogs()
    {
        // Arrange & Act
        var response = new LogQueryResponse();

        // Assert
        response.Logs.Should().NotBeNull();
        response.Logs.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        response.IsTruncated.Should().BeFalse();
    }

    [Fact]
    public void LogQueryResponse_ShouldIndicateTruncation()
    {
        // Arrange & Act
        var response = new LogQueryResponse
        {
            Logs = Enumerable.Range(1, 1000)
                .Select(i => new LogQueryResult
                {
                    Id = $"id-{i}",
                    Timestamp = DateTimeOffset.Now,
                    Application = "App",
                    Level = "Info",
                    Message = $"Message {i}"
                })
                .ToList(),
            TotalCount = 5000,
            IsTruncated = true
        };

        // Assert
        response.Logs.Should().HaveCount(1000);
        response.TotalCount.Should().Be(5000);
        response.IsTruncated.Should().BeTrue();
    }
}
