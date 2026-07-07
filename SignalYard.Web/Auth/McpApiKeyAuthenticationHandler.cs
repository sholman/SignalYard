using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SignalYard.Web.Auth;

/// <summary>
/// Authentication handler for the MCP endpoint. Validates a single global key
/// (configured via <see cref="McpApiKeyAuthenticationDefaults.ConfigurationKey"/>) supplied either
/// in the <c>X-Api-Key</c> header or as an <c>Authorization: Bearer &lt;key&gt;</c> token.
///
/// The key is compared in constant time and the handler fails closed: if no key is configured,
/// every request is rejected.
/// </summary>
public class McpApiKeyAuthenticationHandler : AuthenticationHandler<McpApiKeyAuthenticationOptions>
{
    private readonly IConfiguration _configuration;

    public McpApiKeyAuthenticationHandler(
        IOptionsMonitor<McpApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = _configuration[McpApiKeyAuthenticationDefaults.ConfigurationKey];

        // Fail closed: an unset/blank key means the MCP endpoint is disabled, never open.
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            Logger.LogWarning("MCP request rejected: no {ConfigKey} is configured.", McpApiKeyAuthenticationDefaults.ConfigurationKey);
            return Task.FromResult(AuthenticateResult.Fail("MCP access is not configured."));
        }

        var presentedKey = ExtractKey();
        if (string.IsNullOrEmpty(presentedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!FixedTimeEquals(presentedKey, configuredKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid MCP API key."));
        }

        // Success. This key can read every application's logs, so record an audit line.
        Logger.LogInformation("MCP request authenticated as investigator from {RemoteIp}.",
            Context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "McpInvestigator"),
            new Claim(ClaimTypes.AuthenticationMethod, McpApiKeyAuthenticationDefaults.AuthenticationScheme)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Returns a plain 401 instead of the default challenge, which for the OIDC default scheme
    /// would redirect to a browser login page - useless for a headless MCP client.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{McpApiKeyAuthenticationDefaults.AuthenticationScheme}";
        return Task.CompletedTask;
    }

    /// <summary>Reads the key from the X-Api-Key header, falling back to Authorization: Bearer.</summary>
    private string? ExtractKey()
    {
        if (Request.Headers.TryGetValue(McpApiKeyAuthenticationDefaults.HeaderName, out var apiKeyHeader))
        {
            var value = apiKeyHeader.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        if (AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authHeader)
            && string.Equals(authHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(authHeader.Parameter))
        {
            return authHeader.Parameter.Trim();
        }

        return null;
    }

    /// <summary>
    /// Constant-time comparison of two keys. Hashing both sides to a fixed length first means the
    /// comparison itself does not leak the length of the configured key.
    /// </summary>
    private static bool FixedTimeEquals(string presented, string configured)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(presentedHash, configuredHash);
    }
}
