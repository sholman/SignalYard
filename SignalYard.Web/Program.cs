using Azure.Data.Tables;
using SignalYard.Core.Services;
using SignalYard.Web.Auth;
using SignalYard.Web.Endpoints;
using SignalYard.Web.Mcp;
using SignalYard.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Configure routing to use lowercase URLs
builder.Services.AddRouting(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

// Add Entra ID authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

builder.Services.AddAuthorization(options =>
{
    // Require authentication for all pages by default
    options.FallbackPolicy = options.DefaultPolicy;
});

// Configure Azure Table Storage using a factory to allow test configuration overrides
builder.Services.AddSingleton<TableServiceClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("TableStorage");
    
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("TableServiceClient is not configured. Set ConnectionStrings:TableStorage in configuration.");
    }
    
    return new TableServiceClient(connectionString);
});

// Register Core services
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<LogStorageService>();
builder.Services.AddSingleton<ApplicationStorageService>();

// Register hosted services
builder.Services.AddHostedService<TableInitializationService>();
builder.Services.AddHostedService<RetentionCleanupService>();

// Add API key authentication handler (per-application ingestion keys)
builder.Services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.AuthenticationScheme,
        options => { });

// Add the global MCP investigator key scheme (guards the /mcp endpoint)
builder.Services.AddAuthentication()
    .AddScheme<McpApiKeyAuthenticationOptions, McpApiKeyAuthenticationHandler>(
        McpApiKeyAuthenticationDefaults.AuthenticationScheme,
        options => { });

// Register the MCP server exposing read-only log investigation tools over Streamable HTTP.
// Stateless mode keeps each request self-contained (POST-only), so it scales across App Service instances.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<LogInvestigationTools>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map API endpoints (these use API key auth)
app.MapIngestEndpoints();

// Map the MCP endpoint. The explicit authorization policy overrides the global OIDC FallbackPolicy,
// so unauthenticated MCP clients get a 401 rather than a browser login redirect.
app.MapMcp("/mcp")
    .RequireAuthorization(policy => policy
        .AddAuthenticationSchemes(McpApiKeyAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser())
    .DisableAntiforgery();

// Map MVC controllers (these use Entra ID auth)

// Friendly URL for the log viewer (served by HomeController).
app.MapControllerRoute(
    name: "logs",
    pattern: "logs/{action=Index}/{id?}",
    defaults: new { controller = "Home" });

// Dashboard is the landing page.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

// Make Program class accessible for WebApplicationFactory
public partial class Program { }
