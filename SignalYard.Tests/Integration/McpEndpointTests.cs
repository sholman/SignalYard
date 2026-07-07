using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
    public async Task Mcp_WithBearerToken_PassesAuthentication()
    {
        var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Mcp:ApiKey", TestKey));
        using var client = factory.CreateClient(NoRedirect);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestKey);

        var response = await client.PostAsync("/mcp", EmptyJsonBody());

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
