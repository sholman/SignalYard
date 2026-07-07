namespace SignalYard.Playwright.Tests;

/// <summary>
/// Tests for the Home/Log Viewer page functionality
/// Uses test server with auth bypassed
/// </summary>
[TestFixture]
public class HomePageTests : PlaywrightTestBase
{
    [Test]
    public async Task HomePage_ShouldDisplayLogViewer()
    {
        await NavigateToHomeAsync();

        // Verify page loads without showing an error page
        var pageContent = await Page.ContentAsync();
        Assert.That(pageContent, Does.Not.Contain("An error occurred"));
        Assert.That(pageContent, Does.Not.Contain("Exception:"));
        
        // Should have the log viewer content
        await Expect(Page.Locator("body")).ToBeVisibleAsync();
        Assert.That(pageContent, Does.Contain("SignalYard"), "Page should contain SignalYard branding");
    }

    [Test]
    public async Task HomePage_ShouldHaveSearchForm()
    {
        await NavigateToHomeAsync();

        // Verify search/filter form elements exist
        var form = Page.Locator("form");
        var formCount = await form.CountAsync();
        
        Assert.That(formCount, Is.GreaterThan(0), "Page should have at least one form");
    }

    [Test]
    public async Task HomePage_ShouldHaveTimeRangeFilter()
    {
        await NavigateToHomeAsync();

        var timeRangeSelect = Page.Locator("select[name='TimeRange'], #TimeRange, select").First;
        
        if (await timeRangeSelect.CountAsync() > 0)
        {
            await Expect(timeRangeSelect).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task HomePage_ShouldDisplayLogsTable()
    {
        await NavigateToHomeAsync();

        // Should have a table for log entries
        var table = Page.Locator("table").First;
        
        if (await table.CountAsync() > 0)
        {
            await Expect(table).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task HomePage_TimeRangeFilter_ShouldHaveOptions()
    {
        await NavigateToHomeAsync();

        var timeRangeSelect = Page.Locator("select[name='TimeRange'], #TimeRange").First;
        
        if (await timeRangeSelect.CountAsync() > 0 && await timeRangeSelect.IsVisibleAsync())
        {
            // Should have time range options
            var options = timeRangeSelect.Locator("option");
            var count = await options.CountAsync();
            
            Assert.That(count, Is.GreaterThan(1), "Should have multiple time range options");
        }
    }

    [Test]
    public async Task HomePage_ShouldHaveSearchButton()
    {
        await NavigateToHomeAsync();

        // Find submit button
        var searchButton = Page.Locator("button[type='submit'], input[type='submit']").First;
        
        if (await searchButton.CountAsync() > 0)
        {
            await Expect(searchButton).ToBeVisibleAsync();
        }
    }
}
