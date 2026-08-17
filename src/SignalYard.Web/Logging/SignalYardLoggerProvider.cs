using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SignalYard.Web.Logging;

/// <summary>
/// ILoggerProvider that mirrors SignalYard's own log events into its built-in application. It depends
/// only on the in-memory <see cref="SelfLogQueue"/> (never on storage), so it is safe to construct during
/// logging bootstrap, before the storage/table services are ready. The background
/// <see cref="SelfLogFlushService"/> owns the storage dependency and drains the queue.
///
/// The <c>ProviderAlias</c> lets advanced operators target it with standard per-category logging config
/// (e.g. "Logging:SignalYard:LogLevel").
/// </summary>
[ProviderAlias("SignalYard")]
public sealed class SignalYardLoggerProvider : ILoggerProvider
{
    private readonly SelfLogQueue _queue;
    private readonly string? _instanceName;

    public SignalYardLoggerProvider(SelfLogQueue queue, IConfiguration configuration)
    {
        _queue = queue;
        var instance = configuration["InstanceName"];
        _instanceName = string.IsNullOrWhiteSpace(instance) ? null : instance;
    }

    public ILogger CreateLogger(string categoryName) =>
        new SignalYardLogger(categoryName, _queue, _instanceName);

    public void Dispose()
    {
    }
}
