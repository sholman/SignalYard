using System.Security.Claims;
using System.Text.Encodings.Web;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using SignalYard.Core.Entities;
using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace SignalYard.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory that bypasses authentication for testing
/// </summary>
public class SignalYardTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment variable for testing connection string before the host is built
        builder.UseSetting("ConnectionStrings:TableStorage", "UseDevelopmentStorage=true");
        
        builder.ConfigureTestServices(services =>
        {
            // Remove hosted services that try to connect to Azure Storage on startup
            // These would fail in CI environments without Azurite running
            services.RemoveAll<IHostedService>();
            
            // Remove existing TableServiceClient
            services.RemoveAll<TableServiceClient>();
            
            // Create a mock TableServiceClient with mock TableClients
            var mockTableServiceClient = CreateMockTableServiceClient();
            services.AddSingleton(mockTableServiceClient);
            
            // Remove and replace the storage services with fresh instances using mocked TableServiceClient
            services.RemoveAll<ApiKeyService>();
            services.RemoveAll<LogStorageService>();
            services.RemoveAll<ApplicationStorageService>();
            
            services.AddSingleton(sp => new ApiKeyService(mockTableServiceClient));
            services.AddSingleton(sp => new LogStorageService(mockTableServiceClient));
            services.AddSingleton(sp => new ApplicationStorageService(
                mockTableServiceClient, 
                sp.GetRequiredService<ApiKeyService>()));
            
            // Remove existing authentication schemes and add test authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
                options.DefaultScheme = "TestScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });

            // Override authorization to allow anonymous by default for UI tests
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = null; // Remove the require-auth fallback
            });
        });

        builder.UseEnvironment("Testing");
    }
    
    private static TableServiceClient CreateMockTableServiceClient()
    {
        var mockTableServiceClient = new Mock<TableServiceClient>();
        
        // Create mock TableClients for each table
        var mockApiKeysTable = CreateMockApiKeysTableClient();
        var mockLogsTable = CreateMockLogsTableClient();
        var mockApplicationsTable = CreateMockApplicationsTableClient();
        
        mockTableServiceClient
            .Setup(x => x.GetTableClient("ApiKeys"))
            .Returns(mockApiKeysTable);
        mockTableServiceClient
            .Setup(x => x.GetTableClient("Logs"))
            .Returns(mockLogsTable);
        mockTableServiceClient
            .Setup(x => x.GetTableClient("Applications"))
            .Returns(mockApplicationsTable);
            
        return mockTableServiceClient.Object;
    }
    
    private static TableClient CreateMockApiKeysTableClient()
    {
        var mockTableClient = new Mock<TableClient>();
        
        // Setup CreateIfNotExistsAsync - just return a completed task with null response
        mockTableClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>>(null!));
        
        // Setup GetEntityAsync to throw 404 for any API key lookup (simulating no valid API keys)
        mockTableClient
            .Setup(x => x.GetEntityAsync<ApiKeyLookup>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Entity not found"));
        
        return mockTableClient.Object;
    }
    
    private static TableClient CreateMockApplicationsTableClient()
    {
        var mockTableClient = new Mock<TableClient>();
        
        // Setup CreateIfNotExistsAsync
        mockTableClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>>(null!));
            
        // Setup QueryAsync<Application> to return empty results
        var emptyPage = Page<Application>.FromValues(
            new List<Application>(), 
            null, 
            Mock.Of<Response>());
        var emptyAsyncPageable = AsyncPageable<Application>.FromPages(new[] { emptyPage });
        
        mockTableClient
            .Setup(x => x.QueryAsync<Application>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(emptyAsyncPageable);
            
        return mockTableClient.Object;
    }
    
    private static TableClient CreateMockLogsTableClient()
    {
        var mockTableClient = new Mock<TableClient>();
        
        // Setup CreateIfNotExistsAsync
        mockTableClient
            .Setup(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>>(null!));
            
        // Setup QueryAsync<LogEntry> to return empty results
        var emptyPage = Page<LogEntry>.FromValues(
            new List<LogEntry>(), 
            null, 
            Mock.Of<Response>());
        var emptyAsyncPageable = AsyncPageable<LogEntry>.FromPages(new[] { emptyPage });
        
        mockTableClient
            .Setup(x => x.QueryAsync<LogEntry>(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(emptyAsyncPageable);
            
        return mockTableClient.Object;
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
