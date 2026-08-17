using SignalYard.Core.Models;
using SignalYard.Core.Services;
using SignalYard.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SignalYard.Web.Controllers;

[Authorize]
public class ApplicationsController : Controller
{
    private readonly ApplicationStorageService _applicationService;
    private readonly LogStorageService _logService;

    public ApplicationsController(ApplicationStorageService applicationService, LogStorageService logService)
    {
        _applicationService = applicationService;
        _logService = logService;
    }

    public async Task<IActionResult> Index()
    {
        var applications = await _applicationService.GetAllApplicationsAsync();
        var viewModel = new ApplicationsViewModel
        {
            Applications = applications
        };
        
        if (TempData["SuccessMessage"] is string successMsg)
            viewModel.SuccessMessage = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            viewModel.ErrorMessage = errorMsg;
        if (TempData["GeneratedApiKey"] is string apiKey)
            viewModel.GeneratedApiKey = apiKey;

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApplicationFormModel form)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please correct the validation errors.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var response = await _applicationService.CreateApplicationAsync(new CreateApplicationRequest
            {
                Name = form.Name!,
                Description = form.Description,
                RetentionDays = form.RetentionDays
            });

            TempData["SuccessMessage"] = $"Application '{form.Name}' created successfully.";
            TempData["GeneratedApiKey"] = response.ApiKey;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string name, ApplicationFormModel form)
    {
        if (string.IsNullOrEmpty(name))
        {
            TempData["ErrorMessage"] = "Application name is required.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            // The built-in system app has no API key lookup to toggle and must stay enabled, so ignore the
            // Enabled field for it (description/retention edits are still allowed).
            var existing = await _applicationService.GetApplicationAsync(name);
            var isSystem = existing?.IsSystem == true;

            await _applicationService.UpdateApplicationAsync(name, new UpdateApplicationRequest
            {
                Description = form.Description,
                RetentionDays = form.RetentionDays,
                Enabled = isSystem ? null : form.Enabled
            });

            TempData["SuccessMessage"] = $"Application '{name}' updated successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateKey(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TempData["ErrorMessage"] = "Application name is required.";
            return RedirectToAction(nameof(Index));
        }

        var target = await _applicationService.GetApplicationAsync(name);
        if (target?.IsSystem == true)
        {
            TempData["ErrorMessage"] = $"'{name}' is a built-in application; it uses in-process logging and has no API key to regenerate.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var response = await _applicationService.RegenerateApiKeyAsync(name);
            if (response != null)
            {
                TempData["SuccessMessage"] = $"API key regenerated for '{name}'.";
                TempData["GeneratedApiKey"] = response.ApiKey;
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearLogs(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TempData["ErrorMessage"] = "Application name is required.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _logService.DeleteAllLogsForApplicationAsync(name);
            TempData["SuccessMessage"] = $"All logs for '{name}' were deleted. The application and its API key are unchanged.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TempData["ErrorMessage"] = "Application name is required.";
            return RedirectToAction(nameof(Index));
        }

        var target = await _applicationService.GetApplicationAsync(name);
        if (target?.IsSystem == true)
        {
            TempData["ErrorMessage"] = $"'{name}' is a built-in application and cannot be deleted. Disable it via SelfLogging:Enabled if you don't want it.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            // Delete the logs first so a successful delete never leaves orphaned log data behind.
            // If the app record were removed first and log deletion then failed, the logs would be
            // stranded (no app to retry against) and would resurface if the name were reused.
            await _logService.DeleteAllLogsForApplicationAsync(name);
            await _applicationService.DeleteApplicationAsync(name);
            TempData["SuccessMessage"] = $"Application '{name}' and all its logs were deleted.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
