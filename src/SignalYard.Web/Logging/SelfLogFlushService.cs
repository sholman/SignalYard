using Microsoft.Extensions.Logging;
using SignalYard.Core.Services;

namespace SignalYard.Web.Logging;

/// <summary>
/// Drains the self-log queue into storage under the built-in application. Runs as a hosted background
/// service registered after <c>TableInitializationService</c>, so the Logs table is ensured before the
/// first flush.
///
/// All failures are swallowed (with a Console fallback) — self-logging must never throw or take down the
/// host, and must keep the host healthy even when storage is unavailable (in which case self-logs are
/// simply dropped). This service's own logging is excluded from capture by a provider filter (see
/// <see cref="SelfLoggingServiceCollectionExtensions"/>), so a failing flush cannot feed itself.
/// </summary>
public sealed class SelfLogFlushService : BackgroundService
{
    private readonly SelfLogQueue _queue;
    private readonly LogStorageService _logStorage;
    private readonly SelfLoggingOptions _options;
    private readonly ILogger<SelfLogFlushService> _logger;
    private long _lastReportedDrops;

    public SelfLogFlushService(
        SelfLogQueue queue,
        LogStorageService logStorage,
        SelfLoggingOptions options,
        ILogger<SelfLogFlushService> logger)
    {
        _queue = queue;
        _logStorage = logStorage;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Self-logging flush service started (application '{Application}', minimum level {Level}).",
            _options.ApplicationName, _options.MinimumLevel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushOnceAsync(stoppingToken);
                ReportDrops();

                // Pause so events accumulate into a batch rather than being written one at a time.
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.FlushIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let self-logging bring down the host. Console (not ILogger) keeps this off the
                // captured path entirely, even if the logging pipeline itself is the problem.
                Console.Error.WriteLine($"[SignalYard self-logging] flush failed: {ex.Message}");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // Best-effort final drain on shutdown so buffered entries aren't lost.
        try
        {
            var remaining = _queue.DrainRemaining();
            if (remaining.Count > 0)
            {
                await _logStorage.IngestLogsAsync(_options.ApplicationName, remaining, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SignalYard self-logging] final drain failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads one batch (waiting for at least one event) and writes it to storage. Returns the number of
    /// events written. Extracted so it can be exercised deterministically in tests without the timing of
    /// the background loop.
    /// </summary>
    internal async Task<int> FlushOnceAsync(CancellationToken cancellationToken)
    {
        var batch = await _queue.ReadBatchAsync(_options.BatchSize, cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        await _logStorage.IngestLogsAsync(_options.ApplicationName, batch, cancellationToken);
        return batch.Count;
    }

    private void ReportDrops()
    {
        var dropped = _queue.DroppedCount;
        if (dropped > _lastReportedDrops)
        {
            _logger.LogWarning(
                "Self-logging dropped {Count} event(s) because the in-memory buffer was full (total {Total}). " +
                "Consider raising SelfLogging:QueueCapacity or the minimum level.",
                dropped - _lastReportedDrops, dropped);
            _lastReportedDrops = dropped;
        }
    }
}
