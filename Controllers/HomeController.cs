using Microsoft.AspNetCore.Mvc;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;

namespace GPIMSWebServer.Controllers
{
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
            return View(activeDevices);
        }

        public IActionResult Device(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("Index");

            var deviceData = _dataService.GetLatestDeviceData(id);
            ViewBag.DeviceId = id;
            return View(deviceData);
        }

        public IActionResult Channels(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

            var deviceData = _dataService.GetLatestDeviceData(deviceId);
            ViewBag.DeviceId = deviceId;
            return View(deviceData?.Channels ?? new List<ChannelData>());
        }

        public IActionResult Monitoring(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return RedirectToAction("Index");

            ViewBag.DeviceId = deviceId;
            return View();
        }
    }
}