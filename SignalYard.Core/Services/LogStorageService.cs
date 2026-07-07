using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;

namespace SignalYard.Core.Services;

/// <summary>
/// Service for storing and querying log entries
/// </summary>
public partial class LogStorageService
{
    private readonly TableClient _logsTable;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [GeneratedRegex(@"\{(@?\w+)(?::[^}]*)?\}")]
    private static partial Regex MessageTemplateRegex();

    public LogStorageService(TableServiceClient tableServiceClient)
    {
        _logsTable = tableServiceClient.GetTableClient("Logs");
    }

    /// <summary>
    /// Ingests a batch of log events for an application
    /// </summary>
    public async Task<IngestResponse> IngestLogsAsync(
        string applicationName,
        IEnumerable<ClefLogEvent> events,
        CancellationToken cancellationToken = default)
    {
        var response = new IngestResponse();
        var batch = new List<TableTransactionAction>();

        foreach (var evt in events)
        {
            try
            {
                var logEntry = ConvertToLogEntry(applicationName, evt);
                batch.Add(new TableTransactionAction(TableTransactionActionType.Add, logEntry));

                // Azure Table Storage batch limit is 100 entities
                if (batch.Count >= 100)
                {
                    await ExecuteBatchAsync(batch, response, cancellationToken);
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.Errors ??= [];
                response.Errors.Add(ex.Message);
            }
        }

        // Execute remaining batch
        if (batch.Count > 0)
        {
            await ExecuteBatchAsync(batch, response, cancellationToken);
        }

        return response;
    }

    private async Task ExecuteBatchAsync(
        List<TableTransactionAction> batch,
        IngestResponse response,
        CancellationToken cancellationToken)
    {
        // Group by partition key since batch operations require same partition
        var partitionGroups = batch.GroupBy(b => ((LogEntry)b.Entity).PartitionKey);

        foreach (var group in partitionGroups)
        {
            try
            {
                var partitionBatch = group.ToList();
                await _logsTable.SubmitTransactionAsync(partitionBatch, cancellationToken);
                response.Ingested += partitionBatch.Count;
            }
            catch (TableTransactionFailedException ex)
            {
                // Handle partial failures
                response.Failed += group.Count();
                response.Errors ??= [];
                response.Errors.Add($"Batch failed for partition: {ex.Message}");
            }
        }
    }

    private static LogEntry ConvertToLogEntry(string applicationName, ClefLogEvent evt)
    {
        var timestamp = evt.Timestamp ?? DateTimeOffset.UtcNow;
        var message = evt.Message 
            ?? GetRenderedMessageFromProperties(evt.Properties) 
            ?? RenderMessageTemplate(evt.MessageTemplate, evt.Properties);

        return new LogEntry
        {
            PartitionKey = LogEntry.CreatePartitionKey(applicationName, timestamp),
            RowKey = LogEntry.CreateRowKey(timestamp),
            LogTimestamp = timestamp,
            Application = applicationName,
            Level = evt.Level ?? "Information",
            Message = message,
            MessageTemplate = evt.MessageTemplate,
            Exception = evt.Exception,
            EventId = evt.EventId,
            Properties = evt.Properties != null ? JsonSerializer.Serialize(evt.Properties, JsonOptions) : null
        };
    }

    /// <summary>
    /// Extracts rendered message from properties if present (some Serilog sinks put it there).
    /// </summary>
    private static string? GetRenderedMessageFromProperties(Dictionary<string, object>? properties)
    {
        if (properties == null)
            return null;

        if (properties.TryGetValue("RenderedMessage", out var rendered))
        {
            return rendered switch
            {
                JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String => jsonElement.GetString(),
                string s => s,
                _ => rendered?.ToString()
            };
        }

        return null;
    }

    /// <summary>
    /// Renders a message template by substituting placeholders with property values.
    /// Handles Serilog CLEF format where @m may be omitted and only @mt is provided.
    /// </summary>
    private static string RenderMessageTemplate(string? template, Dictionary<string, object>? properties)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (properties == null || properties.Count == 0)
            return template;

        return MessageTemplateRegex().Replace(template, match =>
        {
            var propertyName = match.Groups[1].Value;
            
            // Handle destructured properties (prefixed with @)
            if (propertyName.StartsWith('@'))
                propertyName = propertyName[1..];

            if (properties.TryGetValue(propertyName, out var value))
            {
                return value switch
                {
                    JsonElement jsonElement => FormatJsonElement(jsonElement),
                    _ => value?.ToString() ?? string.Empty
                };
            }

            return match.Value; // Keep original placeholder if property not found
        });
    }

    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Maximum number of partition queries to run concurrently.
    /// </summary>
    private const int MaxParallelPartitionQueries = 8;

    /// <summary>
    /// Queries logs based on the provided parameters
    /// </summary>
    public async Task<LogQueryResponse> QueryLogsAsync(
        LogQueryRequest request,
        IEnumerable<string>? allApplicationNames = null,
        CancellationToken cancellationToken = default)
    {
        var response = new LogQueryResponse();

        // Determine which applications to query
        var applicationsToQuery = string.IsNullOrEmpty(request.Application)
            ? allApplicationNames?.ToList() ?? []
            : [request.Application];

        if (applicationsToQuery.Count == 0)
        {
            return response;
        }

        // Get all partition keys (one per app per month) that overlap the date range
        var partitionKeys = applicationsToQuery
            .SelectMany(app => GetPartitionKeysForDateRange(app, request.From, request.To))
            .ToList();

        // Row keys use inverted ticks (newest first), so 'To' is the lower bound and 'From' the upper.
        // Applying this range in the query keeps the scan inside the requested window server-side,
        // instead of reading whole month partitions and filtering by timestamp in memory.
        var lowerBound = LogEntry.CreateRowKeyLowerBound(request.To);
        var upperBound = LogEntry.CreateRowKeyUpperBound(request.From);
        var levelFilter = string.IsNullOrEmpty(request.Level)
            ? string.Empty
            : $" and Level eq '{request.Level}'";

        // Query partitions concurrently (bounded), so multi-application/multi-month queries are not
        // serialized behind each other.
        using var throttler = new SemaphoreSlim(MaxParallelPartitionQueries);
        var partitionTasks = partitionKeys.Select(async partitionKey =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                return await QueryPartitionAsync(
                    partitionKey, lowerBound, upperBound, levelFilter, request.MaxResults, cancellationToken);
            }
            finally
            {
                throttler.Release();
            }
        });

        var partitionResults = await Task.WhenAll(partitionTasks);

        // Merge results from all partitions, newest first, and apply the overall limit.
        var merged = partitionResults
            .SelectMany(r => r.Results)
            .OrderByDescending(r => r.Timestamp)
            .ToList();

        response.IsTruncated = partitionResults.Any(r => r.HitLimit) || merged.Count > request.MaxResults;
        response.Logs = merged.Take(request.MaxResults).ToList();
        response.TotalCount = response.Logs.Count;
        return response;
    }

    /// <summary>
    /// Queries a single partition for up to <paramref name="maxResults"/> in-range entries (newest first).
    /// The row-key range already scopes the results to the requested time window, so no in-memory
    /// timestamp filtering is needed.
    /// </summary>
    private async Task<(List<LogQueryResult> Results, bool HitLimit)> QueryPartitionAsync(
        string partitionKey,
        string lowerBound,
        string upperBound,
        string levelFilter,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var filter = $"PartitionKey eq '{partitionKey}' and RowKey ge '{lowerBound}' and RowKey le '{upperBound}'{levelFilter}";

        var results = new List<LogQueryResult>();
        var hitLimit = false;

        await foreach (var entity in _logsTable.QueryAsync<LogEntry>(
            filter: filter,
            maxPerPage: maxResults,
            cancellationToken: cancellationToken))
        {
            results.Add(ConvertToQueryResult(entity));

            if (results.Count >= maxResults)
            {
                hitLimit = true;
                break;
            }
        }

        return (results, hitLimit);
    }

    private static List<string> GetPartitionKeysForDateRange(string applicationName, DateTimeOffset from, DateTimeOffset to)
    {
        var partitionKeys = new List<string>();

        // Partition keys are UTC-month based (see LogEntry.CreatePartitionKey), so enumerate the
        // covered months in UTC. Enumerating in the query's offset could skip or double a partition
        // when the range crosses a month boundary in UTC but not in local time (or vice versa).
        var fromUtc = from.UtcDateTime;
        var current = new DateTimeOffset(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);

        while (current <= to)
        {
            partitionKeys.Add(LogEntry.CreatePartitionKey(applicationName, current));
            current = current.AddMonths(1);
        }

        return partitionKeys;
    }

    private LogQueryResult ConvertToQueryResult(LogEntry entry)
    {
        var properties = string.IsNullOrEmpty(entry.Properties)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(entry.Properties, JsonOptions);

        // Get the best available message: stored message, RenderedMessage from properties, or render template
        var message = entry.Message;
        if (string.IsNullOrEmpty(message))
        {
            message = GetRenderedMessageFromProperties(properties) 
                ?? RenderMessageTemplate(entry.MessageTemplate, properties);
        }

        return new LogQueryResult
        {
            Id = entry.RowKey,
            Timestamp = entry.LogTimestamp,
            Application = entry.Application,
            Level = entry.Level,
            Message = message,
            MessageTemplate = entry.MessageTemplate,
            Exception = entry.Exception,
            EventId = entry.EventId,
            Properties = properties
        };
    }

    /// <summary>
    /// Gets distinct partition keys for an application (for retention cleanup)
    /// </summary>
    public async Task<List<string>> GetPartitionKeysForApplicationAsync(
        string applicationName,
        CancellationToken cancellationToken = default)
    {
        var partitionKeys = new HashSet<string>();
        var filter = $"PartitionKey ge '{applicationName}_' and PartitionKey lt '{applicationName}`'";

        await foreach (var entity in _logsTable.QueryAsync<LogEntry>(
            filter: filter,
            select: new[] { "PartitionKey" },
            cancellationToken: cancellationToken))
        {
            partitionKeys.Add(entity.PartitionKey);
        }

        return partitionKeys.ToList();
    }

    /// <summary>
    /// Deletes all logs for a specific partition key
    /// </summary>
    public async Task DeletePartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
    {
        var batch = new List<TableTransactionAction>();

        await foreach (var entity in _logsTable.QueryAsync<LogEntry>(
            filter: $"PartitionKey eq '{partitionKey}'",
            select: new[] { "PartitionKey", "RowKey" },
            cancellationToken: cancellationToken))
        {
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));

            if (batch.Count >= 100)
            {
                await _logsTable.SubmitTransactionAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _logsTable.SubmitTransactionAsync(batch, cancellationToken);
        }
    }

    /// <summary>
    /// Ensures the Logs table exists
    /// </summary>
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await _logsTable.CreateIfNotExistsAsync(cancellationToken);
    }
}
