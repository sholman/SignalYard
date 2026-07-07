using Azure;
using Azure.Data.Tables;

namespace SignalYard.Core.Entities;

/// <summary>
/// Lookup table for API key validation.
/// PartitionKey: "ApiKey" (constant)
/// RowKey: SHA256 hash of the API key
/// </summary>
public class ApiKeyLookup : ITableEntity
{
    public const string DefaultPartitionKey = "ApiKey";

    public string PartitionKey { get; set; } = DefaultPartitionKey;

    /// <summary>
    /// SHA256 hash of the API key
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>
    /// The application name this key belongs to
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this key is currently active
    /// </summary>
    public bool Enabled { get; set; } = true;
}
