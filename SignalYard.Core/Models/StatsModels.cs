namespace SignalYard.Core.Models;

/// <summary>
/// Request parameters for aggregating log statistics over a time range.
/// </summary>
public class LogStatsRequest
{
    /// <summary>
    /// Application name (optional - if null, aggregates across all applications).
    /// </summary>
    public string? Application { get; set; }

    /// <summary>
    /// Start of date range (required).
    /// </summary>
    public required DateTimeOffset From { get; set; }

    /// <summary>
    /// End of date range (required).
    /// </summary>
    public required DateTimeOffset To { get; set; }

    /// <summary>
    /// Width of each time-series bucket in minutes (e.g. 60 for hourly).
    /// </summary>
    public int BucketMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum entities to scan per partition before the result is flagged approximate.
    /// Table Storage has no server-side aggregation, so counts require scanning rows;
    /// this bounds the work for very high-volume partitions.
    /// </summary>
    public int MaxScanPerPartition { get; set; } = 200_000;
}

/// <summary>
/// A single time bucket in a log-volume time series.
/// </summary>
public class LogTimeBucket
{
    /// <summary>Inclusive start of the bucket (in the requested range's offset).</summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>Exclusive end of the bucket.</summary>
    public DateTimeOffset End { get; set; }

    /// <summary>Total number of log entries in the bucket.</summary>
    public long Total { get; set; }

    /// <summary>Number of Error + Fatal entries in the bucket.</summary>
    public long Errors { get; set; }

    /// <summary>Number of Warning entries in the bucket.</summary>
    public long Warnings { get; set; }

    /// <summary>All non-error, non-warning entries in the bucket.</summary>
    public long Others => Math.Max(0, Total - Errors - Warnings);
}

/// <summary>
/// Aggregated log counts for a single application.
/// </summary>
public class LogAppStat
{
    public required string Application { get; set; }
    public long Total { get; set; }

    /// <summary>Error + Fatal count.</summary>
    public long Errors { get; set; }

    public long Warnings { get; set; }
}

/// <summary>
/// Aggregated statistics for a log query used to render the dashboard.
/// </summary>
public class LogStatsResponse
{
    /// <summary>Total number of log entries in range.</summary>
    public long TotalCount { get; set; }

    /// <summary>Error + Fatal count.</summary>
    public long ErrorCount { get; set; }

    /// <summary>Warning count.</summary>
    public long WarningCount { get; set; }

    /// <summary>Information count.</summary>
    public long InformationCount { get; set; }

    /// <summary>Exact counts keyed by log level.</summary>
    public Dictionary<string, long> CountsByLevel { get; set; } = new();

    /// <summary>Volume over time, oldest bucket first.</summary>
    public List<LogTimeBucket> Buckets { get; set; } = [];

    /// <summary>Per-application breakdown, highest total first.</summary>
    public List<LogAppStat> Applications { get; set; } = [];

    /// <summary>
    /// True when a partition scan hit its cap, so counts are a lower-bound estimate.
    /// </summary>
    public bool IsApproximate { get; set; }

    /// <summary>Bucket width used, in minutes.</summary>
    public int BucketMinutes { get; set; }
}
