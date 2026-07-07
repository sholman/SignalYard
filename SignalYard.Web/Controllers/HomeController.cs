using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SignalYard.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ApplicationStorageService _applicationService;
    private readonly LogStorageService _logService;

    public HomeController(ApplicationStorageService applicationService, LogStorageService logService)
    {
        _applicationService = applicationService;
        _logService = logService;
    }

    public async Task<IActionResult> Index(
        [FromQuery] string? application = null,
        [FromQuery] string? level = null,
        [FromQuery] string timeRange = "24h",
        [FromQuery] DateTime? customFrom = null,
        [FromQuery] DateTime? customTo = null)
    {
        var applications = await _applicationService.GetAllApplicationsAsync();

        var viewModel = new LogViewerViewModel
        {
            Applications = applications,
            SelectedApplication = application,
            SelectedLevel = level,
            SelectedTimeRange = timeRange,
            CustomFromDate = customFrom,
            CustomToDate = customTo,
            HasSearched = true
        };

        // Auto-search on page load, honouring any drill-down filters from the dashboard.
        try
        {
            var (from, to) = GetDateRange(timeRange, customFrom, customTo);

            var request = new LogQueryRequest
            {
                Application = string.IsNullOrEmpty(application) ? null : application,
                From = from,
                To = to,
                Level = string.IsNullOrEmpty(level) ? null : level,
                MaxResults = 1000
            };

            var allAppNames = string.IsNullOrEmpty(application)
                ? applications.Select(a => a.Name)
                : null;

            var response = await _logService.QueryLogsAsync(request, allAppNames);
            viewModel.Logs = response.Logs;
            viewModel.IsTruncated = response.IsTruncated;
        }
        catch (Exception ex)
        {
            viewModel.ErrorMessage = $"Failed to load logs: {ex.Message}";
        }

        return View(viewModel);
    }

    // Kept as a POST-Redirect-GET safety net. The search form now submits via GET
    // (see Index.cshtml), so the results page is always a bookmarkable, refresh-safe
    // GET. Any stale POST — e.g. a mobile browser re-issuing the last request when it
    // restores a discarded tab after the auth cookie expired — is redirected to the
    // GET equivalent instead of failing the OIDC re-challenge and surfacing as a blank
    // page / file download.
    [HttpPost]
    public IActionResult Search(LogSearchFormModel form)
    {
        return RedirectToAction(nameof(Index), new
        {
            application = form.Application,
            level = form.Level,
            timeRange = form.TimeRange,
            customFrom = form.CustomFrom,
            customTo = form.CustomTo
        });
    }

    [HttpGet]
    public async Task<IActionResult> SearchApi([FromQuery] string? application, [FromQuery] string timeRange = "24h", 
        [FromQuery] string? level = null, [FromQuery] DateTime? customFrom = null, [FromQuery] DateTime? customTo = null)
    {
        try
        {
            var applications = await _applicationService.GetAllApplicationsAsync();
            var (from, to) = GetDateRange(timeRange, customFrom, customTo);

            var request = new LogQueryRequest
            {
                Application = string.IsNullOrEmpty(application) ? null : application,
                From = from,
                To = to,
                Level = string.IsNullOrEmpty(level) ? null : level,
                MaxResults = 1000
            };

            var allAppNames = string.IsNullOrEmpty(application)
                ? applications.Select(a => a.Name)
                : null;

            var response = await _logService.QueryLogsAsync(request, allAppNames);
            return Json(new { success = true, logs = response.Logs, isTruncated = response.IsTruncated });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    private static (DateTimeOffset from, DateTimeOffset to) GetDateRange(string timeRange, DateTime? customFrom, DateTime? customTo)
    {
        var now = DateTime.Now;
        var to = new DateTimeOffset(now, TimeZoneInfo.Local.GetUtcOffset(now));

        var from = timeRange switch
        {
            "24h" => to.AddHours(-24),
            "7d" => to.AddDays(-7),
            "30d" => to.AddDays(-30),
            "custom" when customFrom.HasValue => new DateTimeOffset(customFrom.Value, TimeZoneInfo.Local.GetUtcOffset(customFrom.Value)),
            _ => to.AddHours(-24)
        };

        if (timeRange == "custom" && customTo.HasValue)
        {
            to = new DateTimeOffset(customTo.Value, TimeZoneInfo.Local.GetUtcOffset(customTo.Value));
        }

        return (from, to);
    }

    [Route("/Error")]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View();
    }
}
