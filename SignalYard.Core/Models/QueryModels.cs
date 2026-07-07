namespace SignalYard.Core.Models;

/// <summary>
/// Query parameters for retrieving logs
/// </summary>
public class LogQueryRequest
{
    /// <summary>
    /// Application name (optional - if null, queries all applications)
    /// </summary>
    public string? Application { get; set; }

    /// <summary>
    /// Start of date range (required)
    /// </summary>
    public required DateTimeOffset From { get; set; }

    /// <summary>
    /// End of date range (required)
    /// </summary>
    public required DateTimeOffset To { get; set; }

    /// <summary>
    /// Optional filter by log level
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// Maximum number of results (default 1000)
    /// </summary>
    public int MaxResults { get; set; } = 1000;
}

/// <summary>
/// Single log entry returned from a query
/// </summary>
public class LogQueryResult
{
    public required string Id { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required string Application { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Exception { get; set; }
    public string? EventId { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Response model for log queries
/// </summary>
public class LogQueryResponse
{
    /// <summary>
    /// Log entries matching the query
    /// </summary>
    public List<LogQueryResult> Logs { get; set; } = [];

    /// <summary>
    /// Total count of matching logs (may be estimated if truncated)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Whether results were truncated due to max results limit
    /// </summary>
    public bool IsTruncated { get; set; }
}
