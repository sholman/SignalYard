using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalYard.Web.Logging;

/// <summary>
/// Wires up SignalYard's built-in self-logging.
/// </summary>
public static class SelfLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the self-logging queue and <see cref="SignalYardLoggerProvider"/> (plus its provider
    /// filters) when the feature is enabled, and returns the resolved options so the caller can register
    /// the background flush service in the correct hosted-service order. No-op (beyond binding) when
    /// disabled. Reads the "SelfLogging" section; the code-level option defaults are authoritative, so a
    /// config that predates the feature still turns it on.
    /// </summary>
    public static SelfLoggingOptions AddSignalYardSelfLogging(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(SelfLoggingOptions.SectionName)
            .Get<SelfLoggingOptions>() ?? new SelfLoggingOptions();

        if (!options.Enabled)
        {
            return options;
        }

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SelfLogQueue>();

        // The provider only depends on the queue + configuration, so it is safe to build during logging
        // bootstrap.
        builder.Logging.Services.AddSingleton<ILoggerProvider, SignalYardLoggerProvider>();

        // Recursion guard: never capture logs emitted by the self-logging pipeline itself, so a failing
        // flush (which logs a warning) can't feed itself.
        builder.Logging.AddFilter<SignalYardLoggerProvider>("SignalYard.Web.Logging", LogLevel.None);
        // Everything else is captured from the configured minimum level up (default Warning).
        builder.Logging.AddFilter<SignalYardLoggerProvider>(null, options.MinimumLevel);

        return options;
    }
}
