using System.Security.Claims;
using System.Text.Encodings.Web;
using SignalYard.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SignalYard.Web.Auth;

/// <summary>
/// Authentication handler for API key-based authentication on ingestion endpoints.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly ApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for the API key header
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var apiKeyHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = apiKeyHeader.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("API key is empty.");
        }

        // Validate the API key
        var apiKeyLookup = await _apiKeyService.ValidateApiKeyAsync(apiKey);
        if (apiKeyLookup == null)
        {
            return AuthenticateResult.Fail("Invalid or disabled API key.");
        }

        // Create claims for the authenticated application
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKeyLookup.ApplicationName),
            new Claim("ApplicationName", apiKeyLookup.ApplicationName),
            new Claim(ClaimTypes.AuthenticationMethod, ApiKeyAuthenticationDefaults.AuthenticationScheme)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
