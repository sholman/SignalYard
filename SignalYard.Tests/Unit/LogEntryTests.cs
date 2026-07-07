using FluentAssertions;
using SignalYard.Core.Entities;

namespace SignalYard.Tests.Unit;

/// <summary>
/// Unit tests for LogEntry entity and its helper methods
/// </summary>
public class LogEntryTests
{
    [Fact]
    public void CreatePartitionKey_ShouldFormatCorrectly()
    {
        // Arrange
        var applicationName = "MyApp";
        var timestamp = new DateTimeOffset(2025, 1, 17, 12, 30, 0, TimeSpan.Zero);

        // Act
        var partitionKey = LogEntry.CreatePartitionKey(applicationName, timestamp);

        // Assert
        partitionKey.Should().Be("MyApp_202501");
    }

    [Fact]
    public void CreatePartitionKey_ShouldHandleDifferentMonths()
    {
        // Arrange
        var applicationName = "TestApp";
        var december = new DateTimeOffset(2025, 12, 25, 0, 0, 0, TimeSpan.Zero);
        var january = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var decKey = LogEntry.CreatePartitionKey(applicationName, december);
        var janKey = LogEntry.CreatePartitionKey(applicationName, january);

        // Assert
        decKey.Should().Be("TestApp_202512");
        janKey.Should().Be("TestApp_202601");
    }

    [Fact]
    public void CreateRowKey_ShouldProduceUniqueKeys()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var keys = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            keys.Add(LogEntry.CreateRowKey(timestamp));
        }

        // Assert
        keys.Should().HaveCount(100);
    }

    [Fact]
    public void CreateRowKey_ShouldOrderNewerFirst()
    {
        // Arrange
        var olderTimestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newerTimestamp = new DateTimeOffset(2025, 1, 17, 12, 0, 0, TimeSpan.Zero);

        // Act
        var olderKey = LogEntry.CreateRowKey(olderTimestamp);
        var newerKey = LogEntry.CreateRowKey(newerTimestamp);

        // Assert - Newer should sort before older (inverted ticks)
        string.Compare(newerKey, olderKey, StringComparison.Ordinal).Should().BeLessThan(0);
    }

    [Fact]
    public void CreateRowKey_ShouldHaveCorrectFormat()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        var rowKey = LogEntry.CreateRowKey(timestamp);

        // Assert
        rowKey.Should().Contain("_");
        var parts = rowKey.Split('_');
        parts.Should().HaveCount(2);
        parts[0].Should().HaveLength(19); // 19-digit inverted ticks
        parts[1].Should().HaveLength(32); // 32-char GUID without hyphens
    }

    [Fact]
    public void RowKeyBounds_ShouldIncludeKeysWithinRange()
    {
        // Arrange
        var from = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero);

        var lowerBound = LogEntry.CreateRowKeyLowerBound(to);
        var upperBound = LogEntry.CreateRowKeyUpperBound(from);

        // Keys at the exact boundaries and in the middle of the range
        var atFrom = LogEntry.CreateRowKey(from);
        var atTo = LogEntry.CreateRowKey(to);
        var middle = LogEntry.CreateRowKey(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero));

        // Assert - every in-range key satisfies: lowerBound <= key <= upperBound (the query's RowKey ge/le)
        foreach (var key in new[] { atFrom, atTo, middle })
        {
            string.Compare(key, lowerBound, StringComparison.Ordinal).Should().BeGreaterOrEqualTo(0);
            string.Compare(key, upperBound, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
        }
    }

    [Fact]
    public void RowKeyBounds_ShouldExcludeKeysOutsideRange()
    {
        // Arrange
        var from = new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero);

        var lowerBound = LogEntry.CreateRowKeyLowerBound(to);
        var upperBound = LogEntry.CreateRowKeyUpperBound(from);

        // Just older than the range (before From) and just newer than the range (after To)
        var beforeFrom = LogEntry.CreateRowKey(from.AddSeconds(-1));
        var afterTo = LogEntry.CreateRowKey(to.AddSeconds(1));

        // Assert - older-than-range sorts after the upper bound; newer-than-range sorts before the lower bound
        string.Compare(beforeFrom, upperBound, StringComparison.Ordinal).Should().BeGreaterThan(0);
        string.Compare(afterTo, lowerBound, StringComparison.Ordinal).Should().BeLessThan(0);
    }

    [Fact]
    public void CreatePartitionKey_ShouldUseUtcMonth()
    {
        // 00:30 on 1 Jul at +02:00 is still 30 Jun in UTC, so the partition is the UTC month.
        var timestamp = new DateTimeOffset(2025, 7, 1, 0, 30, 0, TimeSpan.FromHours(2));

        LogEntry.CreatePartitionKey("MyApp", timestamp).Should().Be("MyApp_202506");
    }

    [Fact]
    public void CreateRowKey_ShouldOrderByInstant_RegardlessOfOffset()
    {
        // The same instant expressed in two different offsets must produce the same ordering
        // prefix, otherwise range queries shift by the producer/server offset.
        var utc = new DateTimeOffset(2025, 1, 17, 12, 0, 0, TimeSpan.Zero);
        var plusFive = utc.ToOffset(TimeSpan.FromHours(5));

        var utcPrefix = LogEntry.CreateRowKey(utc).Split('_')[0];
        var plusFivePrefix = LogEntry.CreateRowKey(plusFive).Split('_')[0];

        utcPrefix.Should().Be(plusFivePrefix);
    }

    [Fact]
    public void RowKeyBounds_ShouldMatchByInstant_WhenBoundsUseADifferentOffset()
    {
        // An entry stored in UTC must fall inside a window whose From/To are expressed in another
        // offset but cover the same instant. This is the dashboard drill-down case: buckets are
        // instant-based, so the log query window must be too.
        var instant = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var key = LogEntry.CreateRowKey(instant);

        var from = instant.AddHours(-1).ToOffset(TimeSpan.FromHours(5));
        var to = instant.AddHours(1).ToOffset(TimeSpan.FromHours(5));
        var lowerBound = LogEntry.CreateRowKeyLowerBound(to);
        var upperBound = LogEntry.CreateRowKeyUpperBound(from);

        string.Compare(key, lowerBound, StringComparison.Ordinal).Should().BeGreaterOrEqualTo(0);
        string.Compare(key, upperBound, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
    }

    [Fact]
    public void GetApplicationFromPartitionKey_ShouldExtractApplicationName()
    {
        // Arrange
        var partitionKey = "MyApplication_202501";

        // Act
        var appName = LogEntry.GetApplicationFromPartitionKey(partitionKey);

        // Assert
        appName.Should().Be("MyApplication");
    }

    [Fact]
    public void GetApplicationFromPartitionKey_ShouldHandleUnderscoresInName()
    {
        // Arrange
        var partitionKey = "My_Complex_App_202501";

        // Act
        var appName = LogEntry.GetApplicationFromPartitionKey(partitionKey);

        // Assert
        appName.Should().Be("My_Complex_App");
    }

    [Fact]
    public void GetApplicationFromPartitionKey_ShouldReturnFullKeyIfNoUnderscore()
    {
        // Arrange
        var partitionKey = "InvalidPartitionKey";

        // Act
        var appName = LogEntry.GetApplicationFromPartitionKey(partitionKey);

        // Assert
        appName.Should().Be("InvalidPartitionKey");
    }

    [Fact]
    public void GetYearMonthFromPartitionKey_ShouldExtractYearMonth()
    {
        // Arrange
        var partitionKey = "MyApp_202501";

        // Act
        var yearMonth = LogEntry.GetYearMonthFromPartitionKey(partitionKey);

        // Assert
        yearMonth.Should().Be("202501");
    }

    [Fact]
    public void GetYearMonthFromPartitionKey_ShouldReturnEmptyIfNoUnderscore()
    {
        // Arrange
        var partitionKey = "InvalidKey";

        // Act
        var yearMonth = LogEntry.GetYearMonthFromPartitionKey(partitionKey);

        // Assert
        yearMonth.Should().BeEmpty();
    }

    [Fact]
    public void LogEntry_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var entry = new LogEntry();

        // Assert
        entry.PartitionKey.Should().BeEmpty();
        entry.RowKey.Should().BeEmpty();
        entry.Application.Should().BeEmpty();
        entry.Level.Should().BeEmpty();
        entry.Message.Should().BeEmpty();
        entry.MessageTemplate.Should().BeNull();
        entry.Exception.Should().BeNull();
        entry.EventId.Should().BeNull();
        entry.Properties.Should().BeNull();
    }

    [Fact]
    public void LogEntry_ShouldStoreAllProperties()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        var entry = new LogEntry
        {
            PartitionKey = "TestApp_202501",
            RowKey = "1234567890123456789_abc123",
            LogTimestamp = timestamp,
            Application = "TestApp",
            Level = "Error",
            Message = "Test message",
            MessageTemplate = "Test {Property}",
            Exception = "System.Exception: Test",
            EventId = "evt-001",
            Properties = "{\"Property\":\"value\"}"
        };

        // Assert
        entry.PartitionKey.Should().Be("TestApp_202501");
        entry.RowKey.Should().Be("1234567890123456789_abc123");
        entry.LogTimestamp.Should().Be(timestamp);
        entry.Application.Should().Be("TestApp");
        entry.Level.Should().Be("Error");
        entry.Message.Should().Be("Test message");
        entry.MessageTemplate.Should().Be("Test {Property}");
        entry.Exception.Should().Be("System.Exception: Test");
        entry.EventId.Should().Be("evt-001");
        entry.Properties.Should().Be("{\"Property\":\"value\"}");
    }
}
