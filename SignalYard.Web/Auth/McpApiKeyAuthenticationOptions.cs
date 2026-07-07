using Microsoft.AspNetCore.Authentication;

namespace SignalYard.Web.Auth;

/// <summary>
/// Constants for the MCP (Model Context Protocol) API key authentication scheme.
/// This scheme guards the <c>/mcp</c> endpoint with a single, global "investigator" key
/// (configured via <c>Mcp:ApiKey</c>) that grants read-only access to every application's logs.
/// It is intentionally separate from the per-application <c>sy_</c> ingestion keys.
/// </summary>
public static class McpApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "McpApiKey";

    /// <summary>Header carrying the raw key (same header the ingestion endpoints use).</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>Configuration key holding the global MCP investigator key.</summary>
    public const string ConfigurationKey = "Mcp:ApiKey";
}

public class McpApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
