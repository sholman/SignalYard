using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SignalYard.Tests.Integration;

/// <summary>
/// Integration tests for pages that require authentication
/// Uses SignalYardTestFactory which bypasses auth for testing
/// </summary>
public class AuthenticatedPagesTests : IClassFixture<SignalYardTestFactory>
{
    private readonly SignalYardTestFactory _factory;
    private readonly HttpClient _client;

    public AuthenticatedPagesTests(SignalYardTestFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Dashboard")]
    [InlineData("/Dashboard/Index")]
    public async Task DashboardPage_WithTestAuth_ShouldReturnSuccess(string url)
    {
        // Act
        var response = await _client.GetAsync(url);

        // Assert - Should return OK since auth is bypassed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/logs")]
    [InlineData("/Home")]
    [InlineData("/Home/Index")]
    public async Task LogsPage_WithTestAuth_ShouldReturnSuccess(string url)
    {
        // Act
        var response = await _client.GetAsync(url);

        // Assert - Should return OK since auth is bypassed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/Applications")]
    [InlineData("/Applications/Index")]
    public async Task ApplicationsPage_WithTestAuth_ShouldReturnSuccess(string url)
    {
        // Act
        var response = await _client.GetAsync(url);

        // Assert - Should return OK since auth is bypassed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DashboardPage_ShouldContainDashboardContent()
    {
        // Act
        var response = await _client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("SignalYard");
        content.Should().Contain("Total Logs");
    }

    [Fact]
    public async Task LogsPage_ShouldContainLogViewer()
    {
        // Act
        var response = await _client.GetAsync("/logs");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("SignalYard");
    }

    [Fact]
    public async Task ApplicationsPage_ShouldContainApplicationsContent()
    {
        // Act
        var response = await _client.GetAsync("/Applications");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Application");
    }

    [Fact]
    public async Task StaticFiles_ShouldBeAccessible()
    {
        // Act
        var cssResponse = await _client.GetAsync("/app.css");
        var siteResponse = await _client.GetAsync("/site.css");

        // Assert - Static files should be accessible
        cssResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        siteResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
