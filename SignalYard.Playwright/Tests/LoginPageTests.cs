namespace SignalYard.Playwright.Tests;

/// <summary>
/// Tests for page accessibility (with test auth bypassing login)
/// </summary>
[TestFixture]
public class LoginPageTests : PlaywrightTestBase
{
    [Test]
    public async Task HomePage_ShouldBeAccessible()
    {
        // Navigate to the app - should load directly with test auth
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify we get the home page (not redirected to login)
        var pageContent = await Page.ContentAsync();
        
        // Should see the SignalYard app content
        Assert.That(pageContent.Contains("SignalYard") || pageContent.Contains("Log") || pageContent.Contains("Application"), 
            Is.True, "Should display SignalYard content");
    }

    [Test]
    public async Task HomePage_ShouldHaveProperTitle()
    {
        // Navigate to the app
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Get the page title
        var title = await Page.TitleAsync();

        // Title should be meaningful (not empty or error)
        Assert.That(title, Is.Not.Empty);
        Assert.That(title, Does.Not.Contain("Error"));
        Assert.That(title, Does.Not.Contain("404"));
    }

    [Test]
    public async Task HomePage_ShouldNotShowLoginPrompt()
    {
        // Navigate to the app
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should NOT be redirected to login with test auth
        Assert.That(Page.Url, Does.Not.Contain("login"));
        Assert.That(Page.Url, Does.Not.Contain("microsoftonline"));
        Assert.That(Page.Url, Does.Not.Contain("oauth"));
    }
}
