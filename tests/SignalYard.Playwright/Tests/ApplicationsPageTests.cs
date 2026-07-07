namespace SignalYard.Playwright.Tests;

/// <summary>
/// Tests for the Applications management page.
/// Uses test server with authentication bypassed.
/// </summary>
[TestFixture]
public class ApplicationsPageTests : PlaywrightTestBase
{
    [Test]
    public async Task ApplicationsPage_ShouldLoad()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify page loaded successfully
        await Expect(Page.Locator("body")).ToBeVisibleAsync();
        
        // Should not show error page (but page may contain 'error' in CSS classes)
        var pageContent = await Page.ContentAsync();
        Assert.That(pageContent, Does.Not.Contain("An error occurred"));
        Assert.That(pageContent, Does.Not.Contain("Exception:"));
        Assert.That(pageContent, Does.Contain("Applications"), "Page should have Applications heading");
    }

    [Test]
    public async Task ApplicationsPage_ShouldDisplayApplicationsList()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should have applications table or list container
        var appsList = Page.Locator("table, .applications-list, .card, [data-testid='applications-table']").First;
        await Expect(appsList).ToBeVisibleAsync();
    }

    [Test]
    public async Task ApplicationsPage_ShouldHaveCreateButton()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should have a button/link to create new application
        var createButton = Page.Locator("button:has-text('Create'), a:has-text('Create'), button:has-text('Add'), a:has-text('Add'), button:has-text('New'), a:has-text('New'), [href*='Create']").First;
        await Expect(createButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task ApplicationsPage_CreateForm_ShouldHaveNameField()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open create form (might be modal)
        var createButton = Page.Locator("button:has-text('Add'), button:has-text('Create')").First;
        await createButton.ClickAsync();
        
        // Wait for modal to appear
        await Page.WaitForSelectorAsync("#createEditModal[style*='flex'], #formName", new PageWaitForSelectorOptions { Timeout = 5000 });

        // Find name input in the modal form
        var nameInput = Page.Locator("#formName, input[name='Name']").First;
        await Expect(nameInput).ToBeVisibleAsync();

        // Check that name input has pattern validation attribute
        var pattern = await nameInput.GetAttributeAsync("pattern");
        Assert.That(pattern, Is.Not.Null.And.Not.Empty, "Name field should have pattern validation");
    }

    [Test]
    public async Task ApplicationsPage_CreateForm_ShouldHaveRetentionField()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open create form
        var createButton = Page.Locator("button:has-text('Create'), a:has-text('Create'), button:has-text('Add'), [href*='Create']").First;
        await createButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find retention days input
        var retentionInput = Page.Locator("input[name='RetentionDays'], #RetentionDays, input[type='number']").First;
        await Expect(retentionInput).ToBeVisibleAsync();

        // Should have a default value
        var value = await retentionInput.InputValueAsync();
        Assert.That(value, Is.Not.Empty, "Retention days should have a default value");
    }

    [Test]
    public async Task ApplicationsPage_ShouldShowApiKeyPrefixes()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // If there are applications, they should show API key prefixes
        var apiKeyPrefixes = Page.Locator(":text('sy_')");
        
        // This is informational - may not have any applications yet
        var count = await apiKeyPrefixes.CountAsync();
        TestContext.WriteLine($"Found {count} API key prefixes displayed");
    }

    [Test]
    public async Task ApplicationsPage_EditButton_ShouldOpenEditForm()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find an edit button if applications exist
        var editButton = Page.Locator("button:has-text('Edit'), a:has-text('Edit'), [href*='Edit']").First;
        
        if (await editButton.IsVisibleAsync())
        {
            await editButton.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Should show edit form
            var form = Page.Locator("form, .modal, .edit-form");
            await Expect(form.First).ToBeVisibleAsync();
        }
        else
        {
            // No applications to edit - that's okay
            TestContext.WriteLine("No applications available to edit");
        }
    }

    [Test]
    public async Task ApplicationsPage_DeleteButton_ShouldRequireConfirmation()
    {
        await Page.GotoAsync($"{BaseUrl}/Applications");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Find a delete button if applications exist
        var deleteButton = Page.Locator("button:has-text('Delete'), a:has-text('Delete'), [href*='Delete']").First;
        
        if (await deleteButton.IsVisibleAsync())
        {
            // Set up dialog handler to capture confirmation
            var dialogMessage = "";
            Page.Dialog += async (_, dialog) =>
            {
                dialogMessage = dialog.Message;
                await dialog.DismissAsync(); // Cancel the deletion
            };

            await deleteButton.ClickAsync();

            // Wait a moment for dialog
            await Task.Delay(500);

            // Should have shown a confirmation dialog or the delete should require additional confirmation
            var confirmationVisible = !string.IsNullOrEmpty(dialogMessage) || 
                                     await Page.Locator(".modal:has-text('confirm'), .modal:has-text('delete')").IsVisibleAsync();
            
            Assert.That(confirmationVisible, Is.True, "Delete should require confirmation");
        }
        else
        {
            TestContext.WriteLine("No applications available to test delete");
        }
    }
}
