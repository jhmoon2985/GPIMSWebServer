using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;
using System.Security.Claims;

namespace GPIMSWebServer.Controllers
{
    [Authorize]
    public class DeviceUpdateController : Controller
    {
        private readonly IDeviceUpdateService _updateService;
        private readonly IUserActivityService _activityService;
        private readonly ILogger<DeviceUpdateController> _logger;

        public DeviceUpdateController(
            IDeviceUpdateService updateService,
            IUserActivityService activityService,
            ILogger<DeviceUpdateController> logger)
        {
            _updateService = updateService;
            _activityService = activityService;
            _logger = logger;
        }

        // GET: DeviceUpdate
        public async Task<IActionResult> Index()
        {
            try
            {
                var devices = await _updateService.GetAvailableDevicesAsync();
                ViewBag.AvailableDevices = devices;
                ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
                
                await LogUserActivity(ActivityType.ViewDevice, "Accessed device update page");
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading device update page");
                TempData["Error"] = "Error loading device update page.";
                return RedirectToAction("Index", "Home");
            }
        }

        // POST: DeviceUpdate/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "MaintenanceOrAdmin")]
        public async Task<IActionResult> Upload(DeviceUpdateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var devices = await _updateService.GetAvailableDevicesAsync();
                ViewBag.AvailableDevices = devices;
                ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
                return View("Index", model);
            }

            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var username = User.Identity?.Name ?? "Unknown";

                var result = await _updateService.CreateUpdateRequestAsync(model, userId, username);

                if (result)
                {
                    await LogUserActivity(ActivityType.CreateUser, $"Started firmware update for device {model.DeviceId} to version {model.TargetVersion}");
                    TempData["Success"] = $"Update request created for device {model.DeviceId}. Update will begin shortly.";
                }
                else
                {
                    TempData["Error"] = "Failed to create update request. Please try again.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating update request for device {model.DeviceId}");
                TempData["Error"] = "An error occurred while creating the update request.";
                
                var devices = await _updateService.GetAvailableDevicesAsync();
                ViewBag.AvailableDevices = devices;
                ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
                return View("Index", model);
            }
        }

        // GET: DeviceUpdate/History
        public async Task<IActionResult> History(string? deviceId = null, int count = 50)
        {
            try
            {
                var history = await _updateService.GetUpdateHistoryAsync(deviceId, count);
                var devices = await _updateService.GetAvailableDevicesAsync();
                
                ViewBag.SelectedDevice = deviceId;
                ViewBag.Count = count;
                ViewBag.AvailableDevices = devices;
                ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";

                await LogUserActivity(ActivityType.ViewDevice, $"Viewed update history" + (!string.IsNullOrEmpty(deviceId) ? $" for device {deviceId}" : ""));

                return View(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading update history");
                TempData["Error"] = "Error loading update history.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: DeviceUpdate/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "MaintenanceOrAdmin")]
        public async Task<IActionResult> Cancel(int id)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var result = await _updateService.CancelUpdateAsync(id, userId);

                if (result)
                {
                    await LogUserActivity(ActivityType.EditUser, $"Cancelled firmware update (ID: {id})");
                    TempData["Success"] = "Update has been cancelled.";
                }
                else
                {
                    TempData["Error"] = "Failed to cancel update. It may already be completed or failed.";
                }

                return RedirectToAction(nameof(History));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling update {id}");
                TempData["Error"] = "An error occurred while cancelling the update.";
                return RedirectToAction(nameof(History));
            }
        }

        // API: Get devices for AJAX
        [HttpGet]
        public async Task<IActionResult> GetDevices()
        {
            try
            {
                var devices = await _updateService.GetAvailableDevicesAsync();
                return Json(devices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting devices list");
                return Json(new List<string>());
            }
        }

        // API: Get update status
        [HttpGet]
        public async Task<IActionResult> GetUpdateStatus(int id)
        {
            try
            {
                var update = await _updateService.GetUpdateByIdAsync(id);
                if (update == null)
                {
                    return NotFound();
                }

                return Json(new
                {
                    Id = update.Id,
                    DeviceId = update.DeviceId,
                    Status = update.Status.ToString(),
                    Progress = update.Progress,
                    ErrorMessage = update.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting update status for {id}");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private async Task LogUserActivity(ActivityType activityType, string description)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User.Identity?.Name;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username) && int.TryParse(userId, out int userIdInt))
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    await _activityService.LogActivityAsync(userIdInt, username, activityType, description, ipAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log user activity");
            }
        }
    }
}