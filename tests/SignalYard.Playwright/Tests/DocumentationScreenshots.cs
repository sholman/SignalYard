using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SignalYard.Core.Models;
using SignalYard.Core.Services;

namespace SignalYard.Playwright.Tests;

/// <summary>
/// Generates the screenshots embedded in the project README.
///
/// This is NOT part of the normal test run — it is marked [Explicit], so it only
/// runs when selected by name. It seeds realistic example data into the store and
/// captures each key page to <c>docs/images/*.png</c> at the solution root.
///
/// Requires Azurite (UseDevelopmentStorage=true) to be running, same as the other
/// Playwright tests. Run it with:
///
///   dotnet test --filter "FullyQualifiedName~DocumentationScreenshots"
/// </summary>
[TestFixture]
[Explicit("Generates README screenshots; requires Azurite. Run manually via --filter.")]
public class DocumentationScreenshots : PlaywrightTestBase
{
    // Larger, retina-quality context so the README images are crisp.
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        DeviceScaleFactor = 2,
    };

    [Test]
    public async Task CaptureReadmeScreenshots()
    {
        await SeedExampleDataAsync();

        var outputDir = GetDocsImagesDirectory();
        Directory.CreateDirectory(outputDir);

        await CaptureAsync($"{BaseUrl}/Dashboard", Path.Combine(outputDir, "dashboard.png"));
        await CaptureAsync($"{BaseUrl}/", Path.Combine(outputDir, "log-viewer.png"));
        await CaptureAsync($"{BaseUrl}/Applications", Path.Combine(outputDir, "applications.png"));

        TestContext.WriteLine($"Screenshots written to {outputDir}");
    }

    private async Task CaptureAsync(string url, string path)
    {
        await Page.GotoAsync(url);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Give any client-side rendering (charts, relative timestamps) a moment to settle.
        await Page.WaitForTimeoutAsync(600);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        TestContext.WriteLine($"Captured {url} -> {path}");
    }

    private static string GetDocsImagesDirectory()
    {
        return Path.Combine(TestServerExtensions.FindSolutionRoot(), "docs", "images");
    }

    private static async Task SeedExampleDataAsync()
    {
        var services = ServerFixture.Services;
        var apiKeys = services.GetRequiredService<ApiKeyService>();
        var apps = services.GetRequiredService<ApplicationStorageService>();
        var logs = services.GetRequiredService<LogStorageService>();

        await apiKeys.EnsureTableExistsAsync();
        await apps.EnsureTableExistsAsync();
        await logs.EnsureTableExistsAsync();

        var appSpecs = new (string Name, string Description, int RetentionDays)[]
        {
            ("checkout-api", "Public checkout & payment API", 30),
            ("web-storefront", "Customer-facing storefront (server-rendered)", 90),
            ("billing-worker", "Background billing & invoicing jobs", 365),
        };

        foreach (var (name, description, retentionDays) in appSpecs)
        {
            try
            {
                await apps.CreateApplicationAsync(new CreateApplicationRequest
                {
                    Name = name,
                    Description = description,
                    RetentionDays = retentionDays,
                });
            }
            catch (InvalidOperationException)
            {
                // Already exists (store not empty) — fine for regenerating screenshots.
            }
        }

        // Deterministic spread of events across the last 24h so the dashboard chart
        // and the "x mins ago" column both look realistic.
        var now = DateTimeOffset.UtcNow;
        var rng = new Random(20260707);

        var perApp = new Dictionary<string, List<ClefLogEvent>>();
        foreach (var spec in appSpecs) perApp[spec.Name] = new List<ClefLogEvent>();

        // Two passes over the sample set at different time offsets => denser history.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var s in Samples)
            {
                var minutesAgo = rng.Next(pass * 600, pass * 600 + 720); // 0-12h, then 10-22h
                var ts = now.AddMinutes(-minutesAgo).AddSeconds(-rng.Next(0, 60));
                perApp[s.App].Add(new ClefLogEvent
                {
                    Timestamp = ts,
                    Level = s.Level,
                    Message = s.Message,
                    MessageTemplate = s.Template,
                    Properties = s.Props,
                    Exception = s.Exception,
                });
            }
        }

        // A few very recent events so the top of the log viewer reads "just now / x mins ago".
        foreach (var s in Samples.Take(6))
        {
            perApp[s.App].Add(new ClefLogEvent
            {
                Timestamp = now.AddMinutes(-rng.Next(0, 25)),
                Level = s.Level,
                Message = s.Message,
                MessageTemplate = s.Template,
                Properties = s.Props,
                Exception = s.Exception,
            });
        }

        foreach (var (name, events) in perApp)
        {
            await logs.IngestLogsAsync(name, events);
        }
    }

    private record Sample(
        string App,
        string Level,
        string Template,
        string Message,
        Dictionary<string, object> Props,
        string? Exception = null);

    private static readonly Sample[] Samples =
    {
        new("checkout-api", "Information",
            "User {UserId} signed in from {IpAddress}",
            "User u_8842 signed in from 203.0.113.7",
            new() { ["UserId"] = "u_8842", ["IpAddress"] = "203.0.113.7" }),
        new("checkout-api", "Information",
            "Order {OrderId} confirmed for customer {CustomerId} ({Amount})",
            "Order ord_5591 confirmed for customer cus_2043 ($128.40)",
            new() { ["OrderId"] = "ord_5591", ["CustomerId"] = "cus_2043", ["Amount"] = "$128.40" }),
        new("checkout-api", "Warning",
            "Payment gateway {Gateway} responded slowly ({ElapsedMs}ms)",
            "Payment gateway stripe responded slowly (2841ms)",
            new() { ["Gateway"] = "stripe", ["ElapsedMs"] = 2841 }),
        new("checkout-api", "Error",
            "Failed to charge card for order {OrderId}",
            "Failed to charge card for order ord_5604",
            new() { ["OrderId"] = "ord_5604" },
            "System.InvalidOperationException: The payment provider returned an unexpected status (503).\n" +
            "   at Checkout.Payments.StripeGateway.ChargeAsync(ChargeRequest request) in /src/Payments/StripeGateway.cs:line 88\n" +
            "   at Checkout.Orders.OrderService.CompleteAsync(Guid orderId) in /src/Orders/OrderService.cs:line 142"),
        new("checkout-api", "Information",
            "Cart {CartId} checked out with {ItemCount} items",
            "Cart cart_7719 checked out with 3 items",
            new() { ["CartId"] = "cart_7719", ["ItemCount"] = 3 }),
        new("checkout-api", "Debug",
            "Idempotency key {Key} resolved from cache",
            "Idempotency key idem_af31 resolved from cache",
            new() { ["Key"] = "idem_af31" }),

        new("web-storefront", "Information",
            "Rendered {Page} in {ElapsedMs}ms",
            "Rendered /product/widget-pro in 84ms",
            new() { ["Page"] = "/product/widget-pro", ["ElapsedMs"] = 84 }),
        new("web-storefront", "Information",
            "Search for {Query} returned {ResultCount} results",
            "Search for \"wireless keyboard\" returned 27 results",
            new() { ["Query"] = "wireless keyboard", ["ResultCount"] = 27 }),
        new("web-storefront", "Warning",
            "Product image {ImageId} missing, served placeholder",
            "Product image img_9920 missing, served placeholder",
            new() { ["ImageId"] = "img_9920" }),
        new("web-storefront", "Warning",
            "Slow database query on {Table} took {ElapsedMs}ms",
            "Slow database query on Products took 1190ms",
            new() { ["Table"] = "Products", ["ElapsedMs"] = 1190 }),
        new("web-storefront", "Error",
            "Unhandled exception rendering {Endpoint}",
            "Unhandled exception rendering /account/orders",
            new() { ["Endpoint"] = "/account/orders" },
            "System.NullReferenceException: Object reference not set to an instance of an object.\n" +
            "   at Storefront.Pages.OrdersModel.OnGetAsync() in /src/Pages/Orders.cshtml.cs:line 57\n" +
            "   at Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.PageActionInvoker.InvokeHandlerMethodAsync()"),
        new("web-storefront", "Information",
            "Session {SessionId} added {Sku} to cart",
            "Session sess_4410 added SKU-WIDGET-PRO to cart",
            new() { ["SessionId"] = "sess_4410", ["Sku"] = "SKU-WIDGET-PRO" }),

        new("billing-worker", "Information",
            "Generated invoice {InvoiceId} for {Amount}",
            "Generated invoice inv_3320 for $1,240.00",
            new() { ["InvoiceId"] = "inv_3320", ["Amount"] = "$1,240.00" }),
        new("billing-worker", "Information",
            "Processed {Count} invoices in {ElapsedMs}ms",
            "Processed 148 invoices in 3620ms",
            new() { ["Count"] = 148, ["ElapsedMs"] = 3620 }),
        new("billing-worker", "Warning",
            "Retry {Attempt} of {MaxAttempts} for invoice {InvoiceId}",
            "Retry 2 of 5 for invoice inv_3327",
            new() { ["Attempt"] = 2, ["MaxAttempts"] = 5, ["InvoiceId"] = "inv_3327" }),
        new("billing-worker", "Error",
            "Failed to sync invoice {InvoiceId} to accounting system",
            "Failed to sync invoice inv_3341 to accounting system",
            new() { ["InvoiceId"] = "inv_3341" },
            "System.Net.Http.HttpRequestException: Response status code does not indicate success: 502 (Bad Gateway).\n" +
            "   at Billing.Accounting.XeroClient.PushInvoiceAsync(Invoice invoice) in /src/Accounting/XeroClient.cs:line 71\n" +
            "   at Billing.Jobs.InvoiceSyncJob.RunAsync(CancellationToken ct) in /src/Jobs/InvoiceSyncJob.cs:line 39"),
        new("billing-worker", "Fatal",
            "Database connection pool exhausted; shedding load",
            "Database connection pool exhausted; shedding load",
            new() { ["PoolSize"] = 100, ["ActiveConnections"] = 100 }),
        new("billing-worker", "Debug",
            "Scheduled next billing run at {NextRun}",
            "Scheduled next billing run at 2026-07-08T02:00:00Z",
            new() { ["NextRun"] = "2026-07-08T02:00:00Z" }),
        new("billing-worker", "Information",
            "Charged subscription {SubscriptionId} ({Plan})",
            "Charged subscription sub_1180 (pro-annual)",
            new() { ["SubscriptionId"] = "sub_1180", ["Plan"] = "pro-annual" }),
    };
}
