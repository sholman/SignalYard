using SignalYard.Core.Models;

namespace SignalYard.Web.Mcp;

/// <summary>
/// Result returned by the <c>query_logs</c> MCP tool. A compact projection of
/// <see cref="LogQueryResponse"/> that keeps the payload (and therefore token cost) small.
/// </summary>
public class LogQueryToolResult
{
    /// <summary>Effective start of the queried range (after defaulting).</summary>
    public DateTimeOffset From { get; set; }

    /// <summary>Effective end of the queried range (after defaulting).</summary>
    public DateTimeOffset To { get; set; }

    /// <summary>Number of entries returned after any search post-filter.</summary>
    public int TotalReturned { get; set; }

    /// <summary>
    /// True when the underlying query hit the max-results limit, meaning older entries in the
    /// range were not returned. Narrow the time range or raise maxResults to see more.
    /// </summary>
    public bool IsTruncated { get; set; }

    /// <summary>Matching log entries, newest first.</summary>
    public List<LogEntrySummary> Entries { get; set; } = [];
}

/// <summary>
/// A single log entry as exposed over MCP. Structured properties are omitted unless the caller
/// explicitly requests them.
/// </summary>
public class LogEntrySummary
{
    public required DateTimeOffset Timestamp { get; set; }
    public required string Application { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? Exception { get; set; }
    public string? EventId { get; set; }
    public Dictionary<string, object>? Properties { get; set; }

    public static LogEntrySummary From(LogQueryResult result, bool includeProperties) => new()
    {
        Timestamp = result.Timestamp,
        Application = result.Application,
        Level = result.Level,
        Message = result.Message,
        Exception = result.Exception,
        EventId = result.EventId,
        Properties = includeProperties ? result.Properties : null
    };
}
