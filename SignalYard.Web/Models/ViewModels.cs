using System.ComponentModel.DataAnnotations;
using SignalYard.Core.Models;

namespace SignalYard.Web.Models;

public class LogViewerViewModel
{
    public List<ApplicationDto> Applications { get; set; } = [];
    public List<LogQueryResult> Logs { get; set; } = [];
    public string? SelectedApplication { get; set; }
    public string SelectedTimeRange { get; set; } = "24h";
    public string? SelectedLevel { get; set; }
    public DateTime? CustomFromDate { get; set; }
    public DateTime? CustomToDate { get; set; }
    public string? SearchText { get; set; }
    public bool HasSearched { get; set; }
    public bool IsTruncated { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LogSearchFormModel
{
    public string? Application { get; set; }
    public string TimeRange { get; set; } = "24h";
    public string? Level { get; set; }
    public DateTime? CustomFrom { get; set; }
    public DateTime? CustomTo { get; set; }
    public string? SearchText { get; set; }
}

public class DashboardViewModel
{
    public List<ApplicationDto> Applications { get; set; } = [];
    public string? SelectedApplication { get; set; }
    public string SelectedTimeRange { get; set; } = "24h";
    public LogStatsResponse Stats { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class ApplicationsViewModel
{
    public List<ApplicationDto> Applications { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? GeneratedApiKey { get; set; }
}

public class ApplicationFormModel
{
    [Required(ErrorMessage = "Name is required")]
    [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Name can only contain letters, numbers, underscores, and hyphens")]
    public string? Name { get; set; }

    public string? Description { get; set; }

    [Range(1, 3650, ErrorMessage = "Retention must be between 1 and 3650 days")]
    public int RetentionDays { get; set; } = 365;

    public bool Enabled { get; set; } = true;
}
