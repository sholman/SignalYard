using SignalYard.Core.Services;

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Table Storage tables.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
