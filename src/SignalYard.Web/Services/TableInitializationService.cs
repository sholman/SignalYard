using SignalYard.Core.Services;
using SignalYard.Web.Logging;

namespace SignalYard.Web.Services;

/// <summary>
/// Background service that ensures all required Azure Table Storage tables exist on startup.
/// </summary>
public class TableInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TableInitializationService> _logger;

    public TableInitializationService(
        IServiceProvider serviceProvider,
        ILogger<TableInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Azure Table Storage tables...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            
            var apiKeyService = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
            var logStorageService = scope.ServiceProvider.GetRequiredService<LogStorageService>();
            var applicationStorageService = scope.ServiceProvider.GetRequiredService<ApplicationStorageService>();

            await Task.WhenAll(
                apiKeyService.EnsureTableExistsAsync(cancellationToken),
                logStorageService.EnsureTableExistsAsync(cancellationToken),
                applicationStorageService.EnsureTableExistsAsync(cancellationToken)
            );

            _logger.LogInformation("Azure Table Storage tables initialized successfully.");

            // Seed the built-in self-logging application (only when the feature is enabled — the options
            // singleton is registered only then). This runs before the flush service starts, so the app
            // row exists for the cross-app query and retention cleanup. It is idempotent and must never
            // block startup, so failures are logged and swallowed.
            var selfLoggingOptions = scope.ServiceProvider.GetService<SelfLoggingOptions>();
            if (selfLoggingOptions is not null)
            {
                try
                {
                    var result = await applicationStorageService.EnsureSystemApplicationAsync(
                        selfLoggingOptions.ApplicationName,
                        selfLoggingOptions.RetentionDays,
                        cancellationToken);

                    if (result == SystemApplicationSeedResult.Adopted)
                    {
                        _logger.LogWarning(
                            "Self-logging adopted the existing application '{Name}' as its built-in log target; " +
                            "its API key and logs are preserved. Set SelfLogging:ApplicationName to use a different name.",
                            selfLoggingOptions.ApplicationName);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Self-logging application '{Name}' is ready ({Result}).",
                            selfLoggingOptions.ApplicationName, result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to ensure the self-logging application '{Name}'. Self-logging will still write, " +
                        "but the application may not appear until this succeeds.",
                        selfLoggingOptions.ApplicationName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Table Storage tables.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
