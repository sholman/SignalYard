using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;
using SignalYard.Core.Entities;

namespace SignalYard.Core.Services;

/// <summary>
/// Service for managing API keys
/// </summary>
public class ApiKeyService
{
    private readonly TableClient _apiKeyTable;
    private const string ApiKeyPrefix = "sy_";
    private const int KeyLength = 32; // 32 bytes = 256 bits of entropy

    public ApiKeyService(TableServiceClient tableServiceClient)
    {
        _apiKeyTable = tableServiceClient.GetTableClient("ApiKeys");
    }

    /// <summary>
    /// Generates a new API key with the sy_ prefix
    /// </summary>
    public string GenerateApiKey()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(KeyLength);
        var base64Key = Convert.ToBase64String(keyBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"{ApiKeyPrefix}{base64Key}";
    }

    /// <summary>
    /// Computes SHA256 hash of an API key
    /// </summary>
    public string HashApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Gets the display prefix for an API key (first 12 characters)
    /// </summary>
    public string GetApiKeyPrefix(string apiKey)
    {
        return apiKey.Length > 12 ? apiKey[..12] : apiKey;
    }

    /// <summary>
    /// Validates an API key and returns the application info if valid
    /// </summary>
    public async Task<ApiKeyLookup?> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith(ApiKeyPrefix))
        {
            return null;
        }

        var hash = HashApiKey(apiKey);
        
        try
        {
            var response = await _apiKeyTable.GetEntityAsync<ApiKeyLookup>(
                ApiKeyLookup.DefaultPartitionKey,
                hash,
                cancellationToken: cancellationToken);
            
            var lookup = response.Value;
            return lookup.Enabled ? lookup : null;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates an API key lookup entry
    /// </summary>
    public async Task CreateApiKeyLookupAsync(string apiKeyHash, string applicationName, CancellationToken cancellationToken = default)
    {
        var lookup = new ApiKeyLookup
        {
            RowKey = apiKeyHash,
            ApplicationName = applicationName,
            Enabled = true
        };

        await _apiKeyTable.AddEntityAsync(lookup, cancellationToken);
    }

    /// <summary>
    /// Deletes an API key lookup entry
    /// </summary>
    public async Task DeleteApiKeyLookupAsync(string apiKeyHash, CancellationToken cancellationToken = default)
    {
        try
        {
            await _apiKeyTable.DeleteEntityAsync(ApiKeyLookup.DefaultPartitionKey, apiKeyHash, cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Already deleted, ignore
        }
    }

    /// <summary>
    /// Updates the enabled status of an API key
    /// </summary>
    public async Task UpdateApiKeyEnabledAsync(string apiKeyHash, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _apiKeyTable.GetEntityAsync<ApiKeyLookup>(
                ApiKeyLookup.DefaultPartitionKey,
                apiKeyHash,
                cancellationToken: cancellationToken);
            
            var lookup = response.Value;
            lookup.Enabled = enabled;
            
            await _apiKeyTable.UpdateEntityAsync(lookup, lookup.ETag, TableUpdateMode.Replace, cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Key doesn't exist, ignore
        }
    }

    /// <summary>
    /// Ensures the ApiKeys table exists
    /// </summary>
    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        await _apiKeyTable.CreateIfNotExistsAsync(cancellationToken);
    }
}
