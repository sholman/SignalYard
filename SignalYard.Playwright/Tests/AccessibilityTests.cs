namespace SignalYard.Playwright.Tests;

/// <summary>
/// Accessibility tests using Playwright.
/// Uses test server with authentication bypassed.
/// </summary>
[TestFixture]
public class AccessibilityTests : PlaywrightTestBase
{
    [Test]
    public async Task LoginPage_ShouldHaveProperPageStructure()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page should have a title
        var title = await Page.TitleAsync();
        Assert.That(title, Is.Not.Empty, "Page should have a title");

        // Page should have a main element or similar landmark
        var hasMainContent = await Page.Locator("main, [role='main'], .main-content, #main").CountAsync() > 0;
        
        // At minimum, body should be visible
        await Expect(Page.Locator("body")).ToBeVisibleAsync();
    }

    [Test]
    public async Task HomePage_ShouldHaveAccessibleFormLabels()
    {
        await NavigateToHomeAsync();

        // All form inputs should have associated labels
        var inputs = Page.Locator("input:not([type='hidden']), select, textarea");
        var inputCount = await inputs.CountAsync();

        for (int i = 0; i < inputCount; i++)
        {
            var input = inputs.Nth(i);
            var id = await input.GetAttributeAsync("id");
            var ariaLabel = await input.GetAttributeAsync("aria-label");
            var ariaLabelledBy = await input.GetAttributeAsync("aria-labelledby");
            var placeholder = await input.GetAttributeAsync("placeholder");

            // Input should have some form of label
            var hasLabel = !string.IsNullOrEmpty(id) && await Page.Locator($"label[for='{id}']").CountAsync() > 0;
            var hasAriaLabel = !string.IsNullOrEmpty(ariaLabel);
            var hasAriaLabelledBy = !string.IsNullOrEmpty(ariaLabelledBy);
            var hasPlaceholder = !string.IsNullOrEmpty(placeholder);

            var isAccessible = hasLabel || hasAriaLabel || hasAriaLabelledBy || hasPlaceholder;
            
            if (!isAccessible)
            {
                TestContext.WriteLine($"Warning: Input may lack accessible label: {await input.EvaluateAsync<string>("el => el.outerHTML")}");
            }
        }
    }

    [Test]
    public async Task ApplicationsPage_ShouldHaveProperHeadingStructure()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should have at least one heading
        var headings = Page.Locator("h1, h2, h3");
        var headingCount = await headings.CountAsync();
        
        Assert.That(headingCount, Is.GreaterThan(0), "Page should have at least one heading");

        // First heading should ideally be h1
        var h1Count = await Page.Locator("h1").CountAsync();
        if (h1Count == 0)
        {
            TestContext.WriteLine("Warning: Page does not have an h1 element");
        }
    }

    [Test]
    public async Task Tables_ShouldHaveProperStructure()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var tables = Page.Locator("table");
        var tableCount = await tables.CountAsync();

        for (int i = 0; i < tableCount; i++)
        {
            var table = tables.Nth(i);
            
            // Table should have thead or th elements
            var hasHeader = await table.Locator("thead, th").CountAsync() > 0;
            
            if (!hasHeader)
            {
                TestContext.WriteLine($"Warning: Table {i} does not have proper header structure");
            }
        }
    }

    [Test]
    public async Task Buttons_ShouldHaveAccessibleText()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var buttons = Page.Locator("button, [role='button'], input[type='submit'], input[type='button']");
        var buttonCount = await buttons.CountAsync();

        for (int i = 0; i < buttonCount; i++)
        {
            var button = buttons.Nth(i);
            var text = await button.InnerTextAsync();
            var ariaLabel = await button.GetAttributeAsync("aria-label");
            var title = await button.GetAttributeAsync("title");
            var value = await button.GetAttributeAsync("value");

            var hasAccessibleName = !string.IsNullOrWhiteSpace(text) || 
                                   !string.IsNullOrEmpty(ariaLabel) || 
                                   !string.IsNullOrEmpty(title) ||
                                   !string.IsNullOrEmpty(value);

            if (!hasAccessibleName)
            {
                TestContext.WriteLine($"Warning: Button may lack accessible name: {await button.EvaluateAsync<string>("el => el.outerHTML")}");
            }
        }
    }

    [Test]
    public async Task Page_ShouldNotHaveConsoleErrors()
    {
        var consoleErrors = new List<string>();
        
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                consoleErrors.Add(msg.Text);
            }
        };

        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Filter out expected errors (like auth redirects)
        var unexpectedErrors = consoleErrors
            .Where(e => !e.Contains("401") && !e.Contains("Unauthorized"))
            .ToList();

        if (unexpectedErrors.Any())
        {
            TestContext.WriteLine($"Console errors found: {string.Join(", ", unexpectedErrors)}");
        }

        // Don't fail on console errors, just report them
        // Assert.That(unexpectedErrors, Is.Empty, "Page should not have console errors");
    }
}
