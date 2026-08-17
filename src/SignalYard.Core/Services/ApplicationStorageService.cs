using Azure;
using Azure.Data.Tables;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;

namespace SignalYard.Core.Services;

/// <summary>
/// Service for managing application configurations
/// </summary>
public class ApplicationStorageService
{
    private readonly TableClient _applicationsTable;
    private readonly ApiKeyService _apiKeyService;

    public ApplicationStorageService(TableServiceClient tableServiceClient, ApiKeyService apiKeyService)
    {
        _applicationsTable = tableServiceClient.GetTableClient("Applications");
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// Creates a new application and generates an API key
    /// </summary>
    public async Task<CreateApplicationResponse> CreateApplicationAsync(
        CreateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check if application already exists
        try
        {
            await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                request.Name,
                cancellationToken: cancellationToken);
            
            throw new InvalidOperationException($"Application '{request.Name}' already exists.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Expected - application doesn't exist
        }

        // Generate API key
        var apiKey = _apiKeyService.GenerateApiKey();
        var apiKeyHash = _apiKeyService.HashApiKey(apiKey);
        var apiKeyPrefix = _apiKeyService.GetApiKeyPrefix(apiKey);

        // Create application entity
        var application = new Application
        {
            Name = request.Name,
            Description = request.Description,
            ApiKeyHash = apiKeyHash,
            ApiKeyPrefix = apiKeyPrefix,
            RetentionDays = request.RetentionDays,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _applicationsTable.AddEntityAsync(application, cancellationToken);

        // Create API key lookup
        await _apiKeyService.CreateApiKeyLookupAsync(apiKeyHash, request.Name, cancellationToken);

        return new CreateApplicationResponse
        {
            Application = ToDto(application),
            ApiKey = apiKey
        };
    }

    /// <summary>
    /// Gets an application by name
    /// </summary>
    public async Task<ApplicationDto?> GetApplicationAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                name,
                cancellationToken: cancellationToken);
            
            return ToDto(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all applications
    /// </summary>
    public async Task<List<ApplicationDto>> GetAllApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var applications = new List<ApplicationDto>();

        await foreach (var app in _applicationsTable.QueryAsync<Application>(
            filter: $"PartitionKey eq '{Application.DefaultPartitionKey}'",
            cancellationToken: cancellationToken))
        {
            applications.Add(ToDto(app));
        }

        return applications.OrderBy(a => a.Name).ToList();
    }

    /// <summary>
    /// Updates an application
    /// </summary>
    public async Task<ApplicationDto?> UpdateApplicationAsync(
        string name,
        UpdateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                name,
                cancellationToken: cancellationToken);
            
            var application = response.Value;

            if (request.Description != null)
            {
                application.Description = request.Description;
            }

            if (request.RetentionDays.HasValue)
            {
                application.RetentionDays = request.RetentionDays.Value;
            }

            if (request.Enabled.HasValue)
            {
                application.Enabled = request.Enabled.Value;

                // Update API key lookup enabled status
                await _apiKeyService.UpdateApiKeyEnabledAsync(
                    application.ApiKeyHash,
                    request.Enabled.Value,
                    cancellationToken);
            }

            await _applicationsTable.UpdateEntityAsync(application, application.ETag, TableUpdateMode.Replace, cancellationToken);

            return ToDto(application);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Regenerates the API key for an application
    /// </summary>
    public async Task<RegenerateApiKeyResponse?> RegenerateApiKeyAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                name,
                cancellationToken: cancellationToken);
            
            var application = response.Value;

            // Delete old API key lookup
            await _apiKeyService.DeleteApiKeyLookupAsync(application.ApiKeyHash, cancellationToken);

            // Generate new API key
            var newApiKey = _apiKeyService.GenerateApiKey();
            var newApiKeyHash = _apiKeyService.HashApiKey(newApiKey);
            var newApiKeyPrefix = _apiKeyService.GetApiKeyPrefix(newApiKey);

            // Update application
            application.ApiKeyHash = newApiKeyHash;
            application.ApiKeyPrefix = newApiKeyPrefix;

            await _applicationsTable.UpdateEntityAsync(application, application.ETag, TableUpdateMode.Replace, cancellationToken);

            // Create new API key lookup
            await _apiKeyService.CreateApiKeyLookupAsync(newApiKeyHash, name, cancellationToken);

            return new RegenerateApiKeyResponse
            {
                ApiKey = newApiKey,
                ApiKeyPrefix = newApiKeyPrefix
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes an application
    /// </summary>
    public async Task<bool> DeleteApplicationAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                name,
                cancellationToken: cancellationToken);
            
            var application = response.Value;

            // Delete API key lookup
            await _apiKeyService.DeleteApiKeyLookupAsync(application.ApiKeyHash, cancellationToken);

            // Delete application
            await _applicationsTable.DeleteEntityAsync(Application.DefaultPartitionKey, name, cancellationToken: cancellationToken);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets applications with retention settings (for cleanup service)
    /// </summary>
    public async Task<List<(string Name, int RetentionDays)>> GetApplicationRetentionSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = new List<(string Name, int RetentionDays)>();

        await foreach (var app in _applicationsTable.QueryAsync<Application>(
            filter: $"PartitionKey eq '{Application.DefaultPartitionKey}'",
            select: new[] { "RowKey", "RetentionDays" },
            cancellationToken: cancellationToken))
        {
            settings.Add((app.Name, app.RetentionDays));
        }

        return settings;
    }

    /// <summary>
    /// Ensures the built-in system application (SignalYard's own logs) exists. Idempotent,
    /// non-destructive, and collision-aware so an in-place upgrade of an existing deployment is seamless:
    /// <list type="bullet">
    /// <item>Absent → create it as a system app with in-process ingestion (no API key) and the supplied retention.</item>
    /// <item>Already a system app → no-op (do not overwrite an operator-tuned retention).</item>
    /// <item>Present but a normal user app of the same name → adopt it in place (mark it system) while
    /// preserving its existing API key, description, and retention.</item>
    /// </list>
    /// Concurrent starts across multiple instances are tolerated (409/412 are treated as "already done").
    /// </summary>
    public async Task<SystemApplicationSeedResult> EnsureSystemApplicationAsync(
        string name,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _applicationsTable.GetEntityAsync<Application>(
                Application.DefaultPartitionKey,
                name,
                cancellationToken: cancellationToken);

            var application = response.Value;

            if (application.IsSystem)
            {
                return SystemApplicationSeedResult.AlreadyPresent;
            }

            // Adopt a pre-existing user app of this name without touching its key, description, or
            // retention. Merge so only IsSystem is written.
            application.IsSystem = true;
            try
            {
                await _applicationsTable.UpdateEntityAsync(
                    application, application.ETag, TableUpdateMode.Merge, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Another instance updated it concurrently; treat as done.
            }

            return SystemApplicationSeedResult.Adopted;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var application = new Application
            {
                Name = name,
                Description = "Built-in application: SignalYard's own internal logs.",
                ApiKeyHash = string.Empty,
                ApiKeyPrefix = string.Empty,
                Enabled = true,
                IsSystem = true,
                RetentionDays = retentionDays,
                CreatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                await _applicationsTable.AddEntityAsync(application, cancellationToken);
                return SystemApplicationSeedResult.Created;
            }
            catch (RequestFailedException addEx) when (addEx.Status == 409)
            {
                // Another instance created it first; fine.
                return SystemApplicationSeedResult.AlreadyPresent;
            }
        }
    }

    /// <summary>
    /// Ensures the Applications table exists
    /// </summary>
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await _applicationsTable.CreateIfNotExistsAsync(cancellationToken);
    }

    private static ApplicationDto ToDto(Application app)
    {
        return new ApplicationDto
        {
            Name = app.Name,
            Description = app.Description,
            ApiKeyPrefix = app.ApiKeyPrefix,
            Enabled = app.Enabled,
            RetentionDays = app.RetentionDays,
            CreatedAt = app.CreatedAt,
            IsSystem = app.IsSystem
        };
    }
}

/// <summary>
/// Outcome of ensuring the built-in system application exists.
/// </summary>
public enum SystemApplicationSeedResult
{
    /// <summary>The system application was created because none existed.</summary>
    Created,

    /// <summary>A system application of this name was already present; nothing changed.</summary>
    AlreadyPresent,

    /// <summary>An existing non-system application of this name was adopted as the system application.</summary>
    Adopted
}
