using SignalYard.Core.Entities;
using SignalYard.Core.Models;

namespace SignalYard.Core.Services;

/// <summary>
/// Statistics/aggregation queries used by the dashboard.
/// Azure Table Storage has no server-side COUNT or GROUP BY, so counts are computed by
/// scanning the in-range rows with a minimal column projection (Level + LogTimestamp only)
/// to keep the payload small.
/// </summary>
public partial class LogStorageService
{
    /// <summary>Columns fetched for aggregation - keep this minimal to reduce transfer size.</summary>
    private static readonly string[] StatsSelect = ["Level", "LogTimestamp"];

    /// <summary>
    /// Aggregates log counts (by level, over time, and per application) for the given range.
    /// </summary>
    public async Task<LogStatsResponse> GetLogStatsAsync(
        LogStatsRequest request,
        IEnumerable<string>? allApplicationNames = null,
        CancellationToken cancellationToken = default)
    {
        var (alignedFrom, bucketCount, buckets) = BuildBuckets(request.From, request.To, request.BucketMinutes);

        var response = new LogStatsResponse
        {
            Buckets = buckets,
            BucketMinutes = request.BucketMinutes
        };

        var applicationsToQuery = string.IsNullOrEmpty(request.Application)
            ? allApplicationNames?.ToList() ?? []
            : [request.Application];

        if (applicationsToQuery.Count == 0)
        {
            return response;
        }

        var partitionKeys = applicationsToQuery
            .SelectMany(app => GetPartitionKeysForDateRange(app, request.From, request.To))
            .ToList();

        // Row keys use inverted ticks (newest first), so 'To' is the lower bound and 'From' the upper.
        var lowerBound = LogEntry.CreateRowKeyLowerBound(request.To);
        var upperBound = LogEntry.CreateRowKeyUpperBound(request.From);

        using var throttler = new SemaphoreSlim(MaxParallelPartitionQueries);
        var tasks = partitionKeys.Select(async partitionKey =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                return await AggregatePartitionAsync(
                    partitionKey, lowerBound, upperBound, alignedFrom,
                    request.BucketMinutes, bucketCount, request.MaxScanPerPartition, cancellationToken);
            }
            finally
            {
                throttler.Release();
            }
        });

        var partitionStats = await Task.WhenAll(tasks);

        // Merge per-partition results.
        var appTotals = new Dictionary<string, LogAppStat>();

        foreach (var stat in partitionStats)
        {
            response.IsApproximate |= stat.HitCap;

            for (var i = 0; i < bucketCount; i++)
            {
                buckets[i].Total += stat.BucketTotal[i];
                buckets[i].Errors += stat.BucketError[i];
                buckets[i].Warnings += stat.BucketWarn[i];
            }

            foreach (var (level, count) in stat.LevelCounts)
            {
                response.CountsByLevel.TryGetValue(level, out var existing);
                response.CountsByLevel[level] = existing + count;
            }

            if (stat.Total == 0)
            {
                continue;
            }

            if (!appTotals.TryGetValue(stat.Application, out var appStat))
            {
                appStat = new LogAppStat { Application = stat.Application };
                appTotals[stat.Application] = appStat;
            }
            appStat.Total += stat.Total;
            appStat.Errors += stat.Errors;
            appStat.Warnings += stat.Warnings;
        }

        response.TotalCount = response.CountsByLevel.Values.Sum();
        response.ErrorCount = LevelCount(response.CountsByLevel, "Error") + LevelCount(response.CountsByLevel, "Fatal");
        response.WarningCount = LevelCount(response.CountsByLevel, "Warning");
        response.InformationCount = LevelCount(response.CountsByLevel, "Information");
        response.Applications = appTotals.Values
            .OrderByDescending(a => a.Total)
            .ThenBy(a => a.Application, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return response;
    }

    private static long LevelCount(Dictionary<string, long> counts, string level) =>
        counts.TryGetValue(level, out var value) ? value : 0;

    /// <summary>
    /// Scans a single partition and accumulates counts by bucket and by level.
    /// </summary>
    private async Task<PartitionStats> AggregatePartitionAsync(
        string partitionKey,
        string lowerBound,
        string upperBound,
        DateTimeOffset alignedFrom,
        int bucketMinutes,
        int bucketCount,
        int maxScan,
        CancellationToken cancellationToken)
    {
        var stats = new PartitionStats(LogEntry.GetApplicationFromPartitionKey(partitionKey), bucketCount);
        var filter = $"PartitionKey eq '{partitionKey}' and RowKey ge '{lowerBound}' and RowKey le '{upperBound}'";

        await foreach (var entity in _logsTable.QueryAsync<LogEntry>(
            filter: filter,
            maxPerPage: 1000,
            select: StatsSelect,
            cancellationToken: cancellationToken))
        {
            var level = string.IsNullOrEmpty(entity.Level) ? "Information" : entity.Level;

            // Bucket by local wall-clock relative to the aligned range start.
            var local = entity.LogTimestamp.ToOffset(alignedFrom.Offset);
            var index = (int)Math.Floor((local - alignedFrom).TotalMinutes / bucketMinutes);
            if (index < 0) index = 0;
            if (index >= bucketCount) index = bucketCount - 1;

            stats.BucketTotal[index]++;
            stats.Total++;

            if (level is "Error" or "Fatal")
            {
                stats.BucketError[index]++;
                stats.Errors++;
            }
            else if (level == "Warning")
            {
                stats.BucketWarn[index]++;
                stats.Warnings++;
            }

            stats.LevelCounts.TryGetValue(level, out var levelCount);
            stats.LevelCounts[level] = levelCount + 1;

            if (stats.Total >= maxScan)
            {
                stats.HitCap = true;
                break;
            }
        }

        return stats;
    }

    /// <summary>
    /// Builds evenly spaced buckets aligned to a clock boundary (from local midnight) so labels
    /// fall on tidy times (e.g. hourly buckets start on the hour).
    /// </summary>
    private static (DateTimeOffset alignedFrom, int bucketCount, List<LogTimeBucket> buckets) BuildBuckets(
        DateTimeOffset from, DateTimeOffset to, int bucketMinutes)
    {
        if (bucketMinutes <= 0) bucketMinutes = 60;

        var midnight = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, from.Offset);
        var minutesSinceMidnight = (from - midnight).TotalMinutes;
        var alignedFrom = midnight.AddMinutes(Math.Floor(minutesSinceMidnight / bucketMinutes) * bucketMinutes);

        var totalMinutes = (to - alignedFrom).TotalMinutes;
        var bucketCount = Math.Max(1, (int)Math.Ceiling(totalMinutes / bucketMinutes));
        bucketCount = Math.Min(bucketCount, 1000); // safety guard against pathological ranges

        var buckets = new List<LogTimeBucket>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var start = alignedFrom.AddMinutes((double)i * bucketMinutes);
            buckets.Add(new LogTimeBucket { Start = start, End = start.AddMinutes(bucketMinutes) });
        }

        return (alignedFrom, bucketCount, buckets);
    }

    /// <summary>Mutable per-partition accumulator merged into the final response.</summary>
    private sealed class PartitionStats(string application, int bucketCount)
    {
        public string Application { get; } = application;
        public long[] BucketTotal { get; } = new long[bucketCount];
        public long[] BucketError { get; } = new long[bucketCount];
        public long[] BucketWarn { get; } = new long[bucketCount];
        public Dictionary<string, long> LevelCounts { get; } = new();
        public long Total { get; set; }
        public long Errors { get; set; }
        public long Warnings { get; set; }
        public bool HitCap { get; set; }
    }
}
