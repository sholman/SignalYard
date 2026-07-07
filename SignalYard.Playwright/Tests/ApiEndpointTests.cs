namespace SignalYard.Playwright.Tests;

/// <summary>
/// Tests for the API endpoints using HTTP client (not browser-based)
/// These tests verify authentication without actually ingesting data
/// </summary>
[TestFixture]
public class ApiEndpointTests : PlaywrightTestBase
{
    [Test]
    public async Task IngestApi_WithoutApiKey_ShouldReturn401()
    {
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/ingest",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    Events = Array.Empty<object>()
                },
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json"
                }
            });

        Assert.That(response.Status, Is.EqualTo(401));
    }

    [Test]
    public async Task IngestApi_WithInvalidApiKey_ShouldReturn401()
    {
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/ingest",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    Events = Array.Empty<object>()
                },
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                    ["X-Api-Key"] = "sy_invalid_key_12345"
                }
            });

        Assert.That(response.Status, Is.EqualTo(401));
    }

    [Test]
    public async Task RawEventsApi_WithoutApiKey_ShouldReturn401()
    {
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/events/raw",
            new APIRequestContextOptions
            {
                Data = "",
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-ndjson"
                }
            });

        Assert.That(response.Status, Is.EqualTo(401));
    }

    [Test]
    public async Task RawEventsApi_WithInvalidApiKey_ShouldReturn401()
    {
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/events/raw",
            new APIRequestContextOptions
            {
                Data = "",
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-ndjson",
                    ["X-Api-Key"] = "sy_invalid_key_12345"
                }
            });

        Assert.That(response.Status, Is.EqualTo(401));
    }
}
