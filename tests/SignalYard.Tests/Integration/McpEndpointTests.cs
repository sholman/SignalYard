using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalYard.Tests.Integration;

/// <summary>
/// Integration tests for the /mcp endpoint's authentication gate.
///
/// These focus on the security boundary: the endpoint must reject unauthenticated/invalid requests
/// with a plain 401 (never an OIDC login redirect, which would be useless to a headless MCP client)
/// and must let a correctly-keyed request through to the MCP handler.
/// </summary>
public class McpEndpointTests : IClassFixture<SignalYardTestFactory>
{
    private const string TestKey = "test-mcp-key";
    private readonly SignalYardTestFactory _factory;

    public McpEndpointTests(SignalYardTestFactory factory)
    {
        _factory = factory;
    }

    private static WebApplicationFactoryClientOptions NoRedirect => new() { AllowAutoRedirect = false };

    private static StringContent EmptyJsonBody() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Mcp_WithoutApiKey_ReturnsUnauthorizedNotRedirect()
    {
        // No Mcp:ApiKey configured on the default factory, and no key presented.
        using var client = _factory.CreateClient(NoRedirect);

        var response = await client.PostAsync("/mcp", EmptyJsonBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Crucially, the global OIDC FallbackPolicy must NOT kick in with a 302 login redirect.
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Mcp_WithWrongApiKey_ReturnsUnauthorized()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Mcp:ApiKey", TestKey));
        using var client = factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Add("X-Api-Key", "not-the-key");

        var response = await client.PostAsync("/mcp", EmptyJsonBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Mcp_WithCorrectApiKey_PassesAuthentication()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Mcp:ApiKey", TestKey));
        using var client = factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Add("X-Api-Key", TestKey);

        var response = await client.PostAsync("/mcp", EmptyJsonBody());

        // We only assert the request got PAST the auth gate; the MCP transport may reject the
        // (deliberately minimal) body with its own 4xx, but it must not be Unauthorized.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_GetRequest_ReturnsMethodNotAllowedNotRedirect()
    {
        // The server runs the MCP transport in stateless (POST-only) mode, so it offers no
        // server->client SSE stream. A GET (which HTTP MCP clients use to open that stream) must
        // return a clean 405 so the client stops trying — never a 302 OIDC login redirect, which
        // the client can't follow and which drives an endless reconnect loop.
        //
        // This reproduces PRODUCTION auth: a global OIDC-style FallbackPolicy that redirects (302)
        // unauthenticated requests. Without the explicit GET /mcp short-circuit, routing yields a
        // synthetic "405" endpoint carrying no auth metadata, so the fallback policy hijacks it into
        // a 302 login redirect — the exact bug that caused a client reconnect storm.
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = RedirectingChallengeHandler.SchemeName;
                    options.DefaultChallengeScheme = RedirectingChallengeHandler.SchemeName;
                    options.DefaultScheme = RedirectingChallengeHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, RedirectingChallengeHandler>(
                    RedirectingChallengeHandler.SchemeName, _ => { });

                services.AddAuthorization(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder(RedirectingChallengeHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .Build();
                });
            });
        });

        using var client = factory.CreateClient(NoRedirect);

        var response = await client.GetAsync("/mcp");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Mcp_WithBearerToken_PassesAuthentication()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Mcp:ApiKey", TestKey));
        using var client = factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestKey);

        var response = await client.PostAsync("/mcp", EmptyJsonBody());

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// A stand-in for the production OpenID Connect scheme: it never authenticates and, when challenged,
/// issues a 302 redirect to a login page — the behaviour that turns an unguarded /mcp method mismatch
/// into a useless login redirect for headless MCP clients.
/// </summary>
public class RedirectingChallengeHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "RedirectingOidc";

    public RedirectingChallengeHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status302Found;
        Response.Headers.Location = "/MicrosoftIdentity/Account/SignIn";
        return Task.CompletedTask;
    }
}
