using SignalYard.Core.Entities;
using SignalYard.Core.Services;

namespace SignalYard.Web.Services;

/// <summary>
/// Background service that periodically cleans up logs older than the retention period.
/// Runs daily to delete entire monthly partitions that are fully outside the retention window.
/// </summary>
public class RetentionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetentionCleanupService> _logger;
    private readonly IConfiguration _configuration;

    public RetentionCleanupService(
        IServiceProvider serviceProvider,
        ILogger<RetentionCleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay startup to avoid running during peak hours
        var startupDelayMinutes = _configuration.GetValue("RetentionCleanup:StartupDelayMinutes", 60);
        _logger.LogInformation("Retention cleanup service will start in {DelayMinutes} minutes.", startupDelayMinutes);
        
        await Task.Delay(TimeSpan.FromMinutes(startupDelayMinutes), stoppingToken);

        var intervalHours = _configuration.GetValue("RetentionCleanup:IntervalHours", 24);
        _logger.LogInformation("Retention cleanup service started. Running every {IntervalHours} hours.", intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during retention cleanup.");
            }

            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting retention cleanup...");

        using var scope = _serviceProvider.CreateScope();
        var applicationService = scope.ServiceProvider.GetRequiredService<ApplicationStorageService>();
        var logStorageService = scope.ServiceProvider.GetRequiredService<LogStorageService>();

        // Get all applications with their retention settings
        var applications = await applicationService.GetApplicationRetentionSettingsAsync(stoppingToken);

        foreach (var (appName, retentionDays) in applications)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await CleanupApplicationLogsAsync(appName, retentionDays, logStorageService, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up logs for application '{ApplicationName}'.", appName);
            }
        }

        _logger.LogInformation("Retention cleanup completed.");
    }

    private async Task CleanupApplicationLogsAsync(
        string applicationName,
        int retentionDays,
        LogStorageService logStorageService,
        CancellationToken stoppingToken)
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        _logger.LogDebug("Checking logs for '{Application}' older than {CutoffDate}.", applicationName, cutoffDate);

        // Get all partition keys for this application
        var partitionKeys = await logStorageService.GetPartitionKeysForApplicationAsync(applicationName, stoppingToken);

        foreach (var partitionKey in partitionKeys)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            // Extract the year-month from the partition key
            var yearMonth = LogEntry.GetYearMonthFromPartitionKey(partitionKey);
            
            if (!TryParseYearMonth(yearMonth, out var partitionMonth))
            {
                _logger.LogWarning("Could not parse year-month from partition key: {PartitionKey}", partitionKey);
                continue;
            }

            // Check if the entire partition (month) is older than the cutoff
            // A partition is safe to delete if the END of that month is before the cutoff
            var endOfMonth = new DateTimeOffset(partitionMonth.Year, partitionMonth.Month, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMonths(1)
                .AddDays(-1);

            if (endOfMonth < cutoffDate)
            {
                _logger.LogInformation(
                    "Deleting partition {PartitionKey} for application '{Application}' (ends {EndOfMonth}, cutoff {Cutoff}).",
                    partitionKey, applicationName, endOfMonth, cutoffDate);

                await logStorageService.DeletePartitionAsync(partitionKey, stoppingToken);
            }
        }
    }

    private static bool TryParseYearMonth(string yearMonth, out DateTime result)
    {
        result = default;
        
        if (yearMonth.Length != 6)
            return false;

        if (!int.TryParse(yearMonth[..4], out var year) || !int.TryParse(yearMonth[4..], out var month))
            return false;

        if (month < 1 || month > 12 || year < 2000 || year > 2100)
            return false;

        result = new DateTime(year, month, 1);
        return true;
    }
}
