using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalYard.Playwright;

/// <summary>
/// Manages a test server instance for Playwright tests with authentication disabled
/// </summary>
public class TestServerFixture : IDisposable
{
    private IHost? _host;
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        if (_host != null) return;

        // Find the SignalYard.Web project directory (relative to test project output)
        var webProjectPath = TestServerExtensions.FindWebProjectPath();
        
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = webProjectPath,
            WebRootPath = Path.Combine(webProjectPath, "wwwroot")
        });

        // Configure to use a random available port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Add services similar to Program.cs but with test auth
        builder.Services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = true;
        });

        // Use test authentication instead of Entra ID
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "TestScheme";
            options.DefaultChallengeScheme = "TestScheme";
            options.DefaultScheme = "TestScheme";
        })
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });

        // Add MVC with views from the SignalYard.Web assembly
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(SignalYard.Web.Controllers.HomeController).Assembly)
            .AddRazorRuntimeCompilation();

        // No fallback policy - allow anonymous access for testing
        builder.Services.AddAuthorization();

        // Mock the Azure Table Storage services for testing
        builder.Services.AddSingleton<MockTableServiceClient>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<MockTableServiceClient>().AsTableServiceClient());
        builder.Services.AddSingleton<SignalYard.Core.Services.ApiKeyService>();
        builder.Services.AddSingleton<SignalYard.Core.Services.LogStorageService>();
        builder.Services.AddSingleton<SignalYard.Core.Services.ApplicationStorageService>();

        // Add API key authentication handler
        builder.Services.AddAuthentication()
            .AddScheme<SignalYard.Web.Auth.ApiKeyAuthenticationOptions, SignalYard.Web.Auth.ApiKeyAuthenticationHandler>(
                SignalYard.Web.Auth.ApiKeyAuthenticationDefaults.AuthenticationScheme,
                options => { });

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        // Map API endpoints
        SignalYard.Web.Endpoints.IngestEndpoints.MapIngestEndpoints(app);

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        _host = app;
        await _host.StartAsync();

        // Get the actual URL the server is listening on
        var server = _host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses?.Addresses.FirstOrDefault() ?? "http://localhost:5000";
    }

    public void Dispose()
    {
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
    }
}

/// <summary>
/// Test authentication handler that always succeeds with a test user
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "testuser@example.com"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Email, "testuser@example.com"),
            new Claim("name", "Test User"),
        };

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Mock TableServiceClient for testing without Azure
/// </summary>
public class MockTableServiceClient
{
    public Azure.Data.Tables.TableServiceClient AsTableServiceClient()
    {
        // Use Azurite connection string or in-memory mock
        // For now, return a client pointing to local development storage
        return new Azure.Data.Tables.TableServiceClient("UseDevelopmentStorage=true");
    }
}

/// <summary>
/// Helper methods for TestServerFixture
/// </summary>
public static class TestServerExtensions
{
    /// <summary>
    /// Finds the SignalYard.Web project directory by walking up from the current directory
    /// </summary>
    public static string FindWebProjectPath()
    {
        // Start from current directory and look for SignalYard.Web
        var currentDir = Directory.GetCurrentDirectory();
        var searchDir = currentDir;
        
        // Walk up to find the solution root
        while (!string.IsNullOrEmpty(searchDir))
        {
            var webProjectPath = Path.Combine(searchDir, "SignalYard.Web");
            if (Directory.Exists(webProjectPath) && File.Exists(Path.Combine(webProjectPath, "SignalYard.Web.csproj")))
            {
                return webProjectPath;
            }
            
            // Also check if we're in a bin folder and need to go up multiple levels
            var potentialSolutionDir = Path.GetDirectoryName(searchDir);
            if (potentialSolutionDir != null)
            {
                var parentWebPath = Path.Combine(potentialSolutionDir, "SignalYard.Web");
                if (Directory.Exists(parentWebPath) && File.Exists(Path.Combine(parentWebPath, "SignalYard.Web.csproj")))
                {
                    return parentWebPath;
                }
            }
            
            searchDir = Path.GetDirectoryName(searchDir);
        }
        
        // Fallback: Try relative paths from test output directory
        // bin/Debug/net10.0 -> ../../../SignalYard.Web
        var binRelativePath = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", "SignalYard.Web"));
        if (Directory.Exists(binRelativePath))
        {
            return binRelativePath;
        }

        throw new InvalidOperationException($"Could not find SignalYard.Web project directory. Current directory: {currentDir}");
    }
}
