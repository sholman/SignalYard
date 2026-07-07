using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SignalYard.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SignalYard.Tests.Integration;

/// <summary>
/// Integration tests for the Ingest API endpoints
/// Uses SignalYardTestFactory which provides test authentication
/// </summary>
public class IngestEndpointsTests : IClassFixture<SignalYardTestFactory>
{
    private readonly SignalYardTestFactory _factory;
    private readonly HttpClient _client;

    public IngestEndpointsTests(SignalYardTestFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task IngestEndpoint_WithoutApiKey_ShouldReturnUnauthorized()
    {
        // Arrange - empty request to avoid actual ingestion
        var request = new IngestRequest { Events = [] };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ingest", request);

        // Assert - API key auth is still required for ingest endpoints
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestEndpoint_WithInvalidApiKey_ShouldReturnUnauthorized()
    {
        // Arrange
        var request = new IngestRequest { Events = [] };

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "sy_invalidkey123");

        // Act
        var response = await client.PostAsJsonAsync("/api/ingest", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RawEventsEndpoint_WithoutApiKey_ShouldReturnUnauthorized()
    {
        // Arrange - empty content to avoid actual ingestion
        var content = new StringContent("", Encoding.UTF8, "application/x-ndjson");

        // Act
        var response = await _client.PostAsync("/api/events/raw", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RawEventsEndpoint_WithInvalidApiKey_ShouldReturnUnauthorized()
    {
        // Arrange
        var content = new StringContent("", Encoding.UTF8, "application/x-ndjson");
        
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "sy_invalidkey123");

        // Act
        var response = await client.PostAsync("/api/events/raw", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void IngestRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new IngestRequest
        {
            Events =
            [
                new ClefLogEvent
                {
                    Timestamp = new DateTimeOffset(2025, 1, 17, 12, 30, 0, TimeSpan.Zero),
                    MessageTemplate = "User {Username} logged in",
                    Level = "Information",
                    Properties = new Dictionary<string, object> { { "Username", "testuser" } }
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<IngestRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Events.Should().HaveCount(1);
        deserialized.Events[0].MessageTemplate.Should().Be("User {Username} logged in");
    }
}
