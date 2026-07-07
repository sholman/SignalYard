using Azure;
using Azure.Data.Tables;

namespace SignalYard.Core.Entities;

/// <summary>
/// Represents an application registered for log ingestion.
/// PartitionKey: "Application" (constant)
/// RowKey: Application name
/// </summary>
public class Application : ITableEntity
{
    public const string DefaultPartitionKey = "Application";

    public string PartitionKey { get; set; } = DefaultPartitionKey;

    /// <summary>
    /// Application name (unique identifier)
    /// </summary>
    public string RowKey { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    /// <summary>
    /// Application name for display
    /// </summary>
    public string Name
    {
        get => RowKey;
        set => RowKey = value;
    }

    /// <summary>
    /// Optional description of the application
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// SHA256 hash of the API key
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// First 12 characters of the API key for display (e.g., sy_abc123...)
    /// </summary>
    public string ApiKeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Whether log ingestion is active for this application
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Log retention period in days (default 365)
    /// </summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>
    /// When this application was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
