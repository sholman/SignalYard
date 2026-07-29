using Microsoft.Extensions.DependencyInjection;
using SignalYard.Core.Models;
using SignalYard.Core.Services;

namespace SignalYard.Playwright.Tests;

/// <summary>
/// Drives the log viewer's infinite scroll in a real browser: the first page is rendered by the
/// server and further pages are fetched as the sentinel below the list comes into view.
///
/// The application name is deliberately mixed-case — generated query strings are lowercased app-wide,
/// so a paging URL built through Url.Action would silently ask for the wrong partition and stop after
/// the first page.
/// </summary>
[TestFixture]
public class LogViewerPagingTests : PlaywrightTestBase
{
    private const string AppName = "PagingTestApp";
    private const int PageSize = 500;
    private const int SeededLogs = 1200;

    [OneTimeSetUp]
    public async Task SeedLogsAsync()
    {
        var services = ServerFixture.Services;
        var apiKeys = services.GetRequiredService<ApiKeyService>();
        var apps = services.GetRequiredService<ApplicationStorageService>();
        var logs = services.GetRequiredService<LogStorageService>();

        await apiKeys.EnsureTableExistsAsync();
        await apps.EnsureTableExistsAsync();
        await logs.EnsureTableExistsAsync();

        try
        {
            await apps.CreateApplicationAsync(new CreateApplicationRequest { Name = AppName, RetentionDays = 30 });
        }
        catch (InvalidOperationException)
        {
            // Already present from an earlier run.
        }

        // Start from a clean slate so the counts below are exact however often this has run before.
        await logs.DeleteAllLogsForApplicationAsync(AppName);

        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, SeededLogs)
            .Select(i => new ClefLogEvent
            {
                // One a minute going back ~20h, so everything sits inside the default 24h range and
                // the ordering of each entry is unambiguous.
                Timestamp = now.AddMinutes(-i - 1),
                Level = "Information",
                Message = $"Seeded log {i}",
            })
            .ToList();

        await logs.IngestLogsAsync(AppName, events);
    }

    [Test]
    public async Task LogViewer_LoadsOnePageUpFront_ThenTheRestOnScroll()
    {
        await Page.GotoAsync($"{BaseUrl}/Home/Index?application={AppName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var entries = Page.Locator(".log-entry");
        Assert.That(await entries.CountAsync(), Is.EqualTo(PageSize),
            "The first render should be capped at one page.");

        await ScrollToBottomAsync();
        await Expect(entries).ToHaveCountAsync(PageSize * 2, new() { Timeout = 15000 });

        await ScrollToBottomAsync();
        await Expect(entries).ToHaveCountAsync(SeededLogs, new() { Timeout = 15000 });

        // Every seeded entry arrived exactly once — no cursor overlap and nothing skipped.
        var ids = await Page.Locator(".log-entry").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('data-log-id'))");
        Assert.That(ids.Distinct().Count(), Is.EqualTo(SeededLogs), "Pages should not overlap or skip entries.");

        // Still newest-first across the page boundaries.
        var messages = await Page.Locator(".log-entry .log-message-line").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.textContent.trim())");
        Assert.That(messages.First(), Is.EqualTo("Seeded log 0"));
        Assert.That(messages.Last(), Is.EqualTo($"Seeded log {SeededLogs - 1}"));

        await Expect(Page.Locator("#logListFooter")).ToContainTextAsync("end of results");

        // Auto-refresh must not reload the page out from under someone who has scrolled through
        // several pages, since a reload drops back to the first one.
        await Expect(Page.Locator("#autoRefreshText")).ToHaveTextAsync("Auto-refresh paused",
            new() { Timeout = 5000 });
    }

    [Test]
    public async Task LogViewer_FilterAppliesToPagesLoadedLater()
    {
        await Page.GotoAsync($"{BaseUrl}/Home/Index?application={AppName}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The only match sits on the last page. Filtering it out of view leaves nothing on screen, so
        // the viewer must keep pulling pages — and apply the filter to each — until it turns up.
        await Page.FillAsync("#filterText", "Seeded log 1150");
        await ScrollToBottomAsync();

        await Expect(Page.Locator(".log-entry:visible")).ToHaveCountAsync(1, new() { Timeout = 30000 });
        await Expect(Page.Locator("#resultCount")).ToContainTextAsync($"1 of {SeededLogs}");
    }

    private async Task ScrollToBottomAsync()
    {
        await Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
    }
}
