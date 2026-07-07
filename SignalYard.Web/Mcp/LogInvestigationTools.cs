using System.ComponentModel;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using ModelContextProtocol.Server;

namespace SignalYard.Web.Mcp;

/// <summary>
/// Read-only MCP tools that let an AI agent investigate logs across every application in SignalYard.
///
/// Tools are static methods. The two storage services are registered as singletons and are resolved
/// from DI as method parameters by the MCP SDK; the remaining parameters form each tool's input
/// schema. Keeping the methods static avoids the SDK's scoped-DI resolution pitfall (which only
/// affects scoped services, but this is the simplest correct pattern regardless).
/// </summary>
[McpServerToolType]
public class LogInvestigationTools
{
    /// <summary>Range used when the caller does not supply from/to.</summary>
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(24);

    [McpServerTool(Name = "list_applications")]
    [Description("List all applications known to SignalYard, including description, whether ingestion is enabled, and the retention period. Use this first to discover which application names can be queried.")]
    public static async Task<IReadOnlyList<ApplicationDto>> ListApplications(
        ApplicationStorageService applicationStore,
        CancellationToken cancellationToken = default)
    {
        return await applicationStore.GetAllApplicationsAsync(cancellationToken);
    }

    [McpServerTool(Name = "query_logs")]
    [Description("Query log entries for one or all applications within a time range. Returns entries newest-first. If the result is truncated, narrow the time range (or filter by level) rather than raising maxResults.")]
    public static async Task<LogQueryToolResult> QueryLogs(
        LogStorageService logStore,
        ApplicationStorageService applicationStore,
        [Description("Application name to restrict the query to. Omit to search across ALL applications.")]
        string? application = null,
        [Description("Log level filter: Verbose, Debug, Information, Warning, Error, or Fatal. Omit for all levels.")]
        string? level = null,
        [Description("Start of the time range as ISO-8601 (e.g. 2026-07-01T00:00:00Z). Defaults to 24 hours before 'to'.")]
        DateTimeOffset? from = null,
        [Description("End of the time range as ISO-8601. Defaults to the current UTC time.")]
        DateTimeOffset? to = null,
        [Description("Maximum number of entries to return. Default 200; clamped to the range 1-1000.")]
        int maxResults = 200,
        [Description("Optional case-insensitive substring filter over message, exception, template and properties. NOTE: this filters only the entries already fetched (up to maxResults), not the entire time range.")]
        string? search = null,
        [Description("Set true to include each entry's structured Properties. Default false to keep the response small.")]
        bool includeProperties = false,
        CancellationToken cancellationToken = default)
    {
        var effectiveTo = to ?? DateTimeOffset.UtcNow;
        var effectiveFrom = from ?? effectiveTo - DefaultLookback;

        var request = new LogQueryRequest
        {
            Application = NullIfBlank(application),
            Level = NullIfBlank(level),
            From = effectiveFrom,
            To = effectiveTo,
            MaxResults = Math.Clamp(maxResults, 1, 1000)
        };

        var allApplicationNames = await ResolveAllApplicationNamesAsync(
            applicationStore, request.Application, cancellationToken);

        var response = await logStore.QueryLogsAsync(request, allApplicationNames, cancellationToken);

        IEnumerable<LogQueryResult> logs = response.Logs;
        if (!string.IsNullOrWhiteSpace(search))
        {
            logs = logs.Where(log => MatchesSearch(log, search));
        }

        var entries = logs
            .Select(log => LogEntrySummary.From(log, includeProperties))
            .ToList();

        return new LogQueryToolResult
        {
            From = effectiveFrom,
            To = effectiveTo,
            TotalReturned = entries.Count,
            IsTruncated = response.IsTruncated,
            Entries = entries
        };
    }

    [McpServerTool(Name = "get_log_stats")]
    [Description("Aggregate log counts by level, over time, and per application for a time range. Use this for an overview before drilling into individual entries with query_logs. If isApproximate is true, counts are a lower bound (a high-volume partition hit the scan cap).")]
    public static async Task<LogStatsResponse> GetLogStats(
        LogStorageService logStore,
        ApplicationStorageService applicationStore,
        [Description("Application name to restrict to. Omit to aggregate across ALL applications.")]
        string? application = null,
        [Description("Start of the time range as ISO-8601. Defaults to 24 hours before 'to'.")]
        DateTimeOffset? from = null,
        [Description("End of the time range as ISO-8601. Defaults to the current UTC time.")]
        DateTimeOffset? to = null,
        [Description("Width of each time-series bucket in minutes. Default 60 (hourly).")]
        int bucketMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var effectiveTo = to ?? DateTimeOffset.UtcNow;
        var effectiveFrom = from ?? effectiveTo - DefaultLookback;

        var request = new LogStatsRequest
        {
            Application = NullIfBlank(application),
            From = effectiveFrom,
            To = effectiveTo,
            BucketMinutes = bucketMinutes <= 0 ? 60 : bucketMinutes
        };

        var allApplicationNames = await ResolveAllApplicationNamesAsync(
            applicationStore, request.Application, cancellationToken);

        return await logStore.GetLogStatsAsync(request, allApplicationNames, cancellationToken);
    }

    /// <summary>
    /// When no specific application is requested, the storage layer requires the full list of
    /// application names to fan the query out (otherwise it returns nothing). Fetch them here.
    /// </summary>
    private static async Task<IEnumerable<string>?> ResolveAllApplicationNamesAsync(
        ApplicationStorageService applicationStore,
        string? application,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(application))
        {
            return null;
        }

        var applications = await applicationStore.GetAllApplicationsAsync(cancellationToken);
        return applications.Select(a => a.Name).ToList();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool MatchesSearch(LogQueryResult log, string search)
    {
        if (Contains(log.Message, search) ||
            Contains(log.Exception, search) ||
            Contains(log.MessageTemplate, search))
        {
            return true;
        }

        if (log.Properties != null)
        {
            foreach (var (key, value) in log.Properties)
            {
                if (Contains(key, search) || Contains(value?.ToString(), search))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Contains(string? value, string search) =>
        value is not null && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
