namespace SignalYard.Playwright;

/// <summary>
/// Base class for all Playwright tests. Manages test server lifecycle and provides common utilities.
/// </summary>
public class PlaywrightTestBase : PageTest
{
    private static TestServerFixture? _serverFixture;
    private static readonly object _lock = new();

    /// <summary>
    /// Base URL for the application - uses test server or environment variable
    /// </summary>
    protected string BaseUrl
    {
        get
        {
            // If external URL is provided, use it
            var envUrl = Environment.GetEnvironmentVariable("SIGNALYARD_BASE_URL");
            if (!string.IsNullOrEmpty(envUrl))
                return envUrl;

            // Otherwise use the test server
            EnsureServerStarted();
            return _serverFixture!.BaseUrl;
        }
    }

    /// <summary>
    /// The shared test server fixture (started on first access). Exposes the host
    /// service provider so tests can seed example data directly.
    /// </summary>
    protected static TestServerFixture ServerFixture
    {
        get
        {
            EnsureServerStarted();
            return _serverFixture!;
        }
    }

    private static void EnsureServerStarted()
    {
        if (_serverFixture != null) return;

        lock (_lock)
        {
            if (_serverFixture != null) return;

            _serverFixture = new TestServerFixture();
            _serverFixture.StartAsync().GetAwaiter().GetResult();
        }
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true, // Allow self-signed certs in dev
            ViewportSize = new ViewportSize
            {
                Width = 1920,
                Height = 1080
            },
            RecordVideoDir = Environment.GetEnvironmentVariable("PLAYWRIGHT_RECORD_VIDEO") == "true" 
                ? "videos/" 
                : null
        };
    }

    /// <summary>
    /// Takes a screenshot on test failure
    /// </summary>
    [TearDown]
    public async Task TakeScreenshotOnFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            var screenshotPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "screenshots",
                $"{TestContext.CurrentContext.Test.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });

            TestContext.AddTestAttachment(screenshotPath);
        }
    }

    /// <summary>
    /// Navigates to the application and waits for it to be ready
    /// </summary>
    protected async Task NavigateToHomeAsync()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for a toast notification to appear
    /// </summary>
    protected async Task<ILocator> WaitForToastAsync(string? containsText = null)
    {
        var toast = Page.Locator(".toast, .alert, [role='alert']");
        await toast.First.WaitForAsync();
        
        if (containsText != null)
        {
            await Expect(toast).ToContainTextAsync(containsText);
        }
        
        return toast;
    }
}
