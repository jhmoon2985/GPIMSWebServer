using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;
using System.Diagnostics;

namespace GPIMSWebServer.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IDataService _dataService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IDataService dataService, ILogger<HomeController> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var activeDevices = _dataService.GetActiveDevices();
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            ViewBag.UserName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "User";
            
            _logger.LogInformation($"User {User.Identity?.Name} accessed home page with role {ViewBag.UserRole}");
            
            return View(activeDevices);
        }

        public IActionResult Device(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("Index");

            var deviceData = _dataService.GetLatestDeviceData(id);
            ViewBag.DeviceId = id;
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            
            return View(deviceData);
        }

        public IActionResult Channels(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

            var deviceData = _dataService.GetLatestDeviceData(deviceId);
            ViewBag.DeviceId = deviceId;
            ViewBag.UserRole = User.FindFirst("Role")?.Value ?? "Unknown";
            
            return View(deviceData?.Channels ?? new List<ChannelData>());
        }

        public IActionResult Monitoring(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

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
    }
}