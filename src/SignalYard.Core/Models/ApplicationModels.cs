namespace SignalYard.Core.Models;

/// <summary>
/// Request to create a new application
/// </summary>
public class CreateApplicationRequest
{
    /// <summary>
    /// Application name (unique identifier)
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Retention period in days (default 365)
    /// </summary>
    public int RetentionDays { get; set; } = 365;
}

/// <summary>
/// Response after creating an application
/// </summary>
public class CreateApplicationResponse
{
    /// <summary>
    /// The created application details
    /// </summary>
    public required ApplicationDto Application { get; set; }

    /// <summary>
    /// The generated API key (only shown once!)
    /// </summary>
    public required string ApiKey { get; set; }
}

/// <summary>
/// Request to update an application
/// </summary>
public class UpdateApplicationRequest
{
    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Retention period in days
    /// </summary>
    public int? RetentionDays { get; set; }

    /// <summary>
    /// Whether ingestion is enabled
    /// </summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// DTO for application display
/// </summary>
public class ApplicationDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string ApiKeyPrefix { get; set; }
    public required bool Enabled { get; set; }
    public required int RetentionDays { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Response after regenerating an API key
/// </summary>
public class RegenerateApiKeyResponse
{
    /// <summary>
    /// The new API key (only shown once!)
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The new key prefix for display
    /// </summary>
    public required string ApiKeyPrefix { get; set; }
}
