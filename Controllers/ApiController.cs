using Microsoft.AspNetCore.Mvc;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;

namespace GPIMSWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        private readonly IDataService _dataService;
        private readonly ILogger<DeviceController> _logger;

        public DeviceController(IDataService dataService, ILogger<DeviceController> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        [HttpPost("data")]
        public async Task<IActionResult> ReceiveData([FromBody] DeviceData deviceData)
        {
            try
            {
                if (deviceData == null)
                    return BadRequest("Device data cannot be null");

                await _dataService.UpdateDeviceDataAsync(deviceData);
                return Ok(new { success = true, timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving device data");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("devices")]
        public IActionResult GetActiveDevices()
        {
            var devices = _dataService.GetActiveDevices();
            return Ok(devices);
        }

        [HttpGet("device/{deviceId}")]
        public IActionResult GetDeviceData(string deviceId)
        {
            var data = _dataService.GetLatestDeviceData(deviceId);
            if (data == null)
                return NotFound($"Device {deviceId} not found");

            return Ok(data);
        }

        [HttpGet("device/{deviceId}/history")]
        public IActionResult GetDeviceHistory(string deviceId, int count = 100)
        {
            var history = _dataService.GetDeviceDataHistory(deviceId, count);
            return Ok(history);
        }

        [HttpGet("device/{deviceId}/channels")]
        public IActionResult GetChannelData(string deviceId)
        {
            var data = _dataService.GetLatestDeviceData(deviceId);
            if (data == null)
                return NotFound($"Device {deviceId} not found");

            return Ok(data.Channels);
        }
    }
}