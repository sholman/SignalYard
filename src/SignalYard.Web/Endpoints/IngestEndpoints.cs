using System.Text.Json;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Auth;

namespace SignalYard.Web.Endpoints;

/// <summary>
/// Minimal API endpoints for log ingestion.
/// </summary>
public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser())
            .DisableAntiforgery();

        // POST /api/ingest - Batch ingestion of log events
        group.MapPost("/ingest", async (
            IngestRequest request,
            LogStorageService logStorageService,
            HttpContext httpContext) =>
        {
            var applicationName = httpContext.User.FindFirst("ApplicationName")?.Value;
            if (string.IsNullOrEmpty(applicationName))
            {
                return Results.Unauthorized();
            }

            var response = await logStorageService.IngestLogsAsync(applicationName, request.Events);
            return Results.Ok(response);
        });

        // POST /api/events/raw - Newline-delimited JSON ingestion (Serilog HTTP sink compatible)
        group.MapPost("/events/raw", async (
            HttpContext httpContext,
            LogStorageService logStorageService) =>
        {
            var applicationName = httpContext.User.FindFirst("ApplicationName")?.Value;
            if (string.IsNullOrEmpty(applicationName))
            {
                return Results.Unauthorized();
            }

            var events = new List<ClefLogEvent>();
            
            using var reader = new StreamReader(httpContext.Request.Body);
            var content = await reader.ReadToEndAsync();
            
            // Split by newlines and parse each line as a JSON object
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                try
                {
                    var evt = JsonSerializer.Deserialize<ClefLogEvent>(line.Trim());
                    if (evt != null)
                    {
                        events.Add(evt);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            if (events.Count == 0)
            {
                return Results.BadRequest(new IngestResponse 
                { 
                    Ingested = 0, 
                    Failed = 0, 
                    Errors = ["No valid log events found in request body."] 
                });
            }

            var response = await logStorageService.IngestLogsAsync(applicationName, events);
            return Results.Ok(response);
        });
    }
}
