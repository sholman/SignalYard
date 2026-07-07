using Azure;
using Azure.Data.Tables;

namespace SignalYard.Core.Entities;

/// <summary>
/// Represents a log entry stored in Azure Table Storage.
/// PartitionKey: {ApplicationName}_{YearMonth} (e.g., MyApp_202501)
/// RowKey: {InvertedTicks}_{Guid} (newest first ordering)
/// </summary>
public class LogEntry : ITableEntity
{
    /// <summary>
    /// Partition key format: {ApplicationName}_{YearMonth}
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Row key format: {InvertedTicks}_{Guid} for newest-first ordering
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>
    /// Original event timestamp from the log event
    /// </summary>
    public DateTimeOffset LogTimestamp { get; set; }

    /// <summary>
    /// Application name that generated this log
    /// </summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>
    /// Log level (Verbose, Debug, Information, Warning, Error, Fatal)
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Rendered message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Original message template
    /// </summary>
    public string? MessageTemplate { get; set; }

    /// <summary>
    /// Exception details if present
    /// </summary>
    public string? Exception { get; set; }

    /// <summary>
    /// Event ID if present
    /// </summary>
    public string? EventId { get; set; }

    /// <summary>
    /// JSON string of additional structured properties
    /// </summary>
    public string? Properties { get; set; }

    /// <summary>
    /// Creates the partition key for a given application and timestamp.
    /// Uses the UTC month so a log's partition is independent of the offset its producer sent
    /// (and matches the UTC-based row-key ordering below).
    /// </summary>
    public static string CreatePartitionKey(string applicationName, DateTimeOffset timestamp)
    {
        return $"{applicationName}_{timestamp.UtcDateTime:yyyyMM}";
    }

    /// <summary>
    /// Creates a row key with inverted ticks for newest-first ordering.
    /// Ordering is by the absolute instant (UTC ticks) so keys are comparable regardless of the
    /// offset each producer stamped its timestamp with, and so range bounds derived from a query's
    /// From/To line up with stored keys no matter what timezone the server runs in.
    /// </summary>
    public static string CreateRowKey(DateTimeOffset timestamp)
    {
        var invertedTicks = DateTimeOffset.MaxValue.Ticks - timestamp.UtcTicks;
        return $"{invertedTicks:D19}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Creates the inclusive lower row-key bound for a range query whose newest point is <paramref name="to"/>.
    /// Row keys use inverted UTC ticks (newest first), so the newest timestamp produces the smallest key.
    /// The bare inverted-ticks prefix sorts at or before every "{invertedTicks}_{guid}" key for that tick,
    /// making it a safe inclusive lower bound regardless of the guid suffix.
    /// </summary>
    public static string CreateRowKeyLowerBound(DateTimeOffset to)
    {
        var invertedTicks = DateTimeOffset.MaxValue.Ticks - to.UtcTicks;
        return $"{invertedTicks:D19}";
    }

    /// <summary>
    /// Creates the inclusive upper row-key bound for a range query whose oldest point is <paramref name="from"/>.
    /// Row keys have the form "{invertedTicks:D19}_{guid}". The separator is '_' (0x5F), so appending the
    /// next character, '`' (0x60), makes the bound sort after every real key sharing that tick — an
    /// inclusive upper bound regardless of the guid suffix or its casing.
    /// </summary>
    public static string CreateRowKeyUpperBound(DateTimeOffset from)
    {
        var invertedTicks = DateTimeOffset.MaxValue.Ticks - from.UtcTicks;
        return $"{invertedTicks:D19}`";
    }

    /// <summary>
    /// Extracts the application name from a partition key
    /// </summary>
    public static string GetApplicationFromPartitionKey(string partitionKey)
    {
        var lastUnderscore = partitionKey.LastIndexOf('_');
        return lastUnderscore > 0 ? partitionKey[..lastUnderscore] : partitionKey;
    }

    /// <summary>
    /// Extracts the year-month from a partition key
    /// </summary>
    public static string GetYearMonthFromPartitionKey(string partitionKey)
    {
        var lastUnderscore = partitionKey.LastIndexOf('_');
        return lastUnderscore > 0 ? partitionKey[(lastUnderscore + 1)..] : string.Empty;
    }
}
