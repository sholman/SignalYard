using Microsoft.Extensions.Logging;
using SignalYard.Core.Models;

namespace SignalYard.Web.Logging;

/// <summary>
/// ILogger that converts each event into a CLEF event (the same shape the HTTP ingestion path produces,
/// so entries render identically in the viewer) and enqueues it for the background flusher. Level and
/// category filtering are handled by the logging framework's provider filters (see
/// <see cref="SelfLoggingServiceCollectionExtensions"/>), so this logger simply enqueues whatever it is
/// handed.
/// </summary>
public sealed class SignalYardLogger : ILogger
{
    private readonly string _category;
    private readonly SelfLogQueue _queue;
    private readonly string? _instanceName;

    public SignalYardLogger(string category, SelfLogQueue queue, string? instanceName)
    {
        _category = category;
        _queue = queue;
        _instanceName = instanceName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        var message = formatter(state, exception);

        // SourceContext mirrors Serilog's convention so the originating class shows in the viewer.
        // Instance stamps which deployment produced the entry — instance identity is otherwise UI-only
        // and not persisted, so multiple instances writing to one built-in app would be indistinguishable.
        var properties = new Dictionary<string, object>
        {
            ["SourceContext"] = _category
        };
        if (_instanceName is not null)
        {
            properties["Instance"] = _instanceName;
        }

        var evt = new ClefLogEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Message = message,
            Level = MapLevel(logLevel),
            Exception = exception?.ToString(),
            EventId = eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name)
                ? (string.IsNullOrEmpty(eventId.Name) ? eventId.Id.ToString() : eventId.Name)
                : null,
            Properties = properties
        };

        _queue.TryEnqueue(evt);
    }

    /// <summary>
    /// Maps <see cref="LogLevel"/> to the Serilog level names the stored log model and the viewer's level
    /// filter expect (Verbose/Debug/Information/Warning/Error/Fatal).
    /// </summary>
    public static string MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "Verbose",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Fatal",
        _ => "Information"
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
