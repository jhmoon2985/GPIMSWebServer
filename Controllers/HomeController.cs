using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;
using System.Diagnostics;
using System.Security.Claims;

namespace GPIMSWebServer.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDataService _dataService;
        private readonly IUserActivityService _activityService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IDataService dataService, IUserActivityService activityService, ILogger<HomeController> logger)
        {
            _dataService = dataService;
            _activityService = activityService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var activeDevices = _dataService.GetActiveDevices();
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            ViewBag.UserName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "User";
            
            _logger.LogInformation($"User {User.Identity?.Name} accessed home page with role {ViewBag.UserRole}");
            
            return View(activeDevices);
        }

        public async Task<IActionResult> Device(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("Index");

            // 디바이스 조회 활동 기록
            await LogUserActivity(ActivityType.ViewDevice, $"Viewed device: {id}");

            var deviceData = _dataService.GetLatestDeviceData(id);
            ViewBag.DeviceId = id;
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            
            return View(deviceData);
        }

        public async Task<IActionResult> Channels(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

            // 채널 조회 활동 기록
            await LogUserActivity(ActivityType.ViewChannels, $"Viewed channels for device: {deviceId}");

            var deviceData = _dataService.GetLatestDeviceData(deviceId);
            ViewBag.DeviceId = deviceId;
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            
            return View(deviceData?.Channels ?? new List<ChannelData>());
        }

        public async Task<IActionResult> Monitoring(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

            // 모니터링 조회 활동 기록
            await LogUserActivity(ActivityType.ViewMonitoring, $"Accessed monitoring for device: {deviceId}");

            ViewBag.DeviceId = deviceId;
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            
            return View();
        }

        // Error page
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // Privacy page (for compliance)
        public IActionResult Privacy()
        {
            return View();
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