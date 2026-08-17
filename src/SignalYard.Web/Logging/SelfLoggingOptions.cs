using Microsoft.Extensions.Logging;

namespace SignalYard.Web.Logging;

/// <summary>
/// Options controlling SignalYard's built-in self-logging, bound from the "SelfLogging" config section.
/// The defaults here are authoritative: an existing deployment whose appsettings.json predates this
/// feature (i.e. has no "SelfLogging" section) still gets these values, so the feature lights up on a
/// simple binary update with no manual config change required.
/// </summary>
public class SelfLoggingOptions
{
    public const string SectionName = "SelfLogging";

    /// <summary>Master on/off switch for self-logging. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum level captured into the built-in application. Default Warning.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// Retention (days) applied to the built-in application when it is first created. Kept short by
    /// default so self-diagnostics cost almost nothing to store. Not re-applied on later startups, so an
    /// operator can tune it in the UI.
    /// </summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>Name of the built-in application these logs are written under. Default "signalyard".</summary>
    public string ApplicationName { get; set; } = "signalyard";

    /// <summary>How often the background flusher drains the queue to storage. Default 2s.</summary>
    public int FlushIntervalSeconds { get; set; } = 2;

    /// <summary>Max events pulled from the queue per storage write. Default 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Max events buffered in memory. When full, new events are dropped rather than blocking the caller,
    /// so logging never adds latency and memory stays bounded. Default 10000.
    /// </summary>
    public int QueueCapacity { get; set; } = 10000;
}
