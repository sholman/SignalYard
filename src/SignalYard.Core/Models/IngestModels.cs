using System.Text.Json.Serialization;

namespace SignalYard.Core.Models;

/// <summary>
/// Represents a single log event in CLEF (Compact Log Event Format) format.
/// Used for Serilog compact JSON ingestion.
/// </summary>
public class ClefLogEvent
{
    /// <summary>
    /// Timestamp (@t)
    /// </summary>
    [JsonPropertyName("@t")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Message template (@mt)
    /// </summary>
    [JsonPropertyName("@mt")]
    public string? MessageTemplate { get; set; }

    /// <summary>
    /// Rendered message (@m)
    /// </summary>
    [JsonPropertyName("@m")]
    public string? Message { get; set; }

    /// <summary>
    /// Log level (@l) - Verbose, Debug, Information, Warning, Error, Fatal
    /// Defaults to Information if not specified
    /// </summary>
    [JsonPropertyName("@l")]
    public string? Level { get; set; }

    /// <summary>
    /// Exception details (@x)
    /// </summary>
    [JsonPropertyName("@x")]
    public string? Exception { get; set; }

    /// <summary>
    /// Event ID (@i)
    /// </summary>
    [JsonPropertyName("@i")]
    public string? EventId { get; set; }

    /// <summary>
    /// Additional properties (captured via JsonExtensionData)
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Request model for batch log ingestion
/// </summary>
public class IngestRequest
{
    /// <summary>
    /// Batch of log events to ingest
    /// </summary>
    public List<ClefLogEvent> Events { get; set; } = [];
}

/// <summary>
/// Response model for log ingestion
/// </summary>
public class IngestResponse
{
    /// <summary>
    /// Number of events successfully ingested
    /// </summary>
    public int Ingested { get; set; }

    /// <summary>
    /// Number of events that failed to ingest
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Error messages if any failures occurred
    /// </summary>
    public List<string>? Errors { get; set; }
}
