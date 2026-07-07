using System.Text.Json;
using FluentAssertions;
using SignalYard.Core.Models;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for CLEF log event parsing
/// </summary>
public class ClefLogEventTests
{
    [Fact]
    public void Deserialize_ShouldParseSerilogClefFormat()
    {
        // Arrange
        var json = """
            {
                "@t": "2025-01-17T12:30:00.000Z",
                "@mt": "User {Username} logged in from {IpAddress}",
                "@m": "User john.doe logged in from 192.168.1.1",
                "@l": "Information",
                "Username": "john.doe",
                "IpAddress": "192.168.1.1"
            }
            """;

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.Timestamp.Should().NotBeNull();
        evt.MessageTemplate.Should().Be("User {Username} logged in from {IpAddress}");
        evt.Message.Should().Be("User john.doe logged in from 192.168.1.1");
        evt.Level.Should().Be("Information");
        evt.Properties.Should().ContainKey("Username");
        evt.Properties.Should().ContainKey("IpAddress");
    }

    [Fact]
    public void Deserialize_ShouldHandleMinimalEvent()
    {
        // Arrange
        var json = """{"@mt": "Simple message"}""";

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.MessageTemplate.Should().Be("Simple message");
        evt.Timestamp.Should().BeNull();
        evt.Level.Should().BeNull();
        evt.Message.Should().BeNull();
    }

    [Fact]
    public void Deserialize_ShouldParseException()
    {
        // Arrange
        var json = """
            {
                "@t": "2025-01-17T12:30:00.000Z",
                "@mt": "An error occurred",
                "@l": "Error",
                "@x": "System.InvalidOperationException: Test exception\n   at TestMethod()"
            }
            """;

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.Level.Should().Be("Error");
        evt.Exception.Should().Contain("System.InvalidOperationException");
    }

    [Fact]
    public void Deserialize_ShouldParseEventId()
    {
        // Arrange
        var json = """
            {
                "@t": "2025-01-17T12:30:00.000Z",
                "@mt": "Request processed",
                "@i": "RequestProcessed"
            }
            """;

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.EventId.Should().Be("RequestProcessed");
    }

    [Fact]
    public void Deserialize_ShouldCaptureAdditionalProperties()
    {
        // Arrange
        var json = """
            {
                "@t": "2025-01-17T12:30:00.000Z",
                "@mt": "Order placed",
                "@l": "Information",
                "OrderId": 12345,
                "CustomerId": "cust-001",
                "TotalAmount": 99.99,
                "Items": ["item1", "item2"]
            }
            """;

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.Properties.Should().NotBeNull();
        evt.Properties.Should().HaveCount(4);
        evt.Properties.Should().ContainKey("OrderId");
        evt.Properties.Should().ContainKey("CustomerId");
        evt.Properties.Should().ContainKey("TotalAmount");
        evt.Properties.Should().ContainKey("Items");
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    public void Deserialize_ShouldParseAllLogLevels(string level)
    {
        // Arrange
        var json = $@"{{""@l"": ""{level}"", ""@mt"": ""Test""}}";

        // Act
        var evt = JsonSerializer.Deserialize<ClefLogEvent>(json);

        // Assert
        evt.Should().NotBeNull();
        evt!.Level.Should().Be(level);
    }

    [Fact]
    public void Deserialize_ShouldHandleNewlineDelimitedEvents()
    {
        // Arrange
        var ndjson = """
            {"@t": "2025-01-17T12:30:00.000Z", "@mt": "Event 1", "@l": "Information"}
            {"@t": "2025-01-17T12:30:01.000Z", "@mt": "Event 2", "@l": "Warning"}
            {"@t": "2025-01-17T12:30:02.000Z", "@mt": "Event 3", "@l": "Error"}
            """;

        // Act
        var events = ndjson
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<ClefLogEvent>(line.Trim()))
            .ToList();

        // Assert
        events.Should().HaveCount(3);
        events[0]!.MessageTemplate.Should().Be("Event 1");
        events[1]!.Level.Should().Be("Warning");
        events[2]!.Level.Should().Be("Error");
    }

    [Fact]
    public void IngestRequest_ShouldContainListOfEvents()
    {
        // Arrange
        var request = new IngestRequest
        {
            Events =
            [
                new ClefLogEvent { MessageTemplate = "Event 1" },
                new ClefLogEvent { MessageTemplate = "Event 2" }
            ]
        };

        // Assert
        request.Events.Should().HaveCount(2);
    }

    [Fact]
    public void IngestResponse_ShouldTrackIngestedAndFailed()
    {
        // Arrange
        var response = new IngestResponse
        {
            Ingested = 10,
            Failed = 2,
            Errors = ["Error 1", "Error 2"]
        };

        // Assert
        response.Ingested.Should().Be(10);
        response.Failed.Should().Be(2);
        response.Errors.Should().HaveCount(2);
    }
}
