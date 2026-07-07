using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SignalYard.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationStorageService _applicationService;
    private readonly LogStorageService _logService;

    public DashboardController(ApplicationStorageService applicationService, LogStorageService logService)
    {
        _applicationService = applicationService;
        _logService = logService;
    }

    public async Task<IActionResult> Index([FromQuery] string? application, [FromQuery] string timeRange = "24h")
    {
        var applications = await _applicationService.GetAllApplicationsAsync();

        var viewModel = new DashboardViewModel
        {
            Applications = applications,
            SelectedApplication = application,
            SelectedTimeRange = timeRange
        };

        try
        {
            var (from, to) = GetDateRange(timeRange);

            var request = new LogStatsRequest
            {
                Application = string.IsNullOrEmpty(application) ? null : application,
                From = from,
                To = to,
                BucketMinutes = GetBucketMinutes(timeRange)
            };

            var allAppNames = string.IsNullOrEmpty(application)
                ? applications.Select(a => a.Name)
                : null;

            viewModel.Stats = await _logService.GetLogStatsAsync(request, allAppNames);
        }
        catch (Exception ex)
        {
            viewModel.ErrorMessage = $"Failed to load dashboard: {ex.Message}";
        }

        return View(viewModel);
    }

    private static int GetBucketMinutes(string timeRange) => timeRange switch
    {
        "24h" => 60,      // hourly
        "7d" => 360,      // 6-hourly
        "30d" => 1440,    // daily
        _ => 60
    };

    private static (DateTimeOffset from, DateTimeOffset to) GetDateRange(string timeRange)
    {
        var now = DateTime.Now;
        var to = new DateTimeOffset(now, TimeZoneInfo.Local.GetUtcOffset(now));

        var from = timeRange switch
        {
            "24h" => to.AddHours(-24),
            "7d" => to.AddDays(-7),
            "30d" => to.AddDays(-30),
            _ => to.AddHours(-24)
        };

        return (from, to);
    }
}
