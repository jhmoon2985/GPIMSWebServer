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

        [HttpGet("devices/all")]
        public IActionResult GetAllKnownDevices()
        {
            var devices = _dataService.GetAllKnownDevices();
            return Ok(devices);
        }

        [HttpGet("devices/status")]
        public IActionResult GetDevicesStatus()
        {
            var statusList = _dataService.GetDeviceStatusSummary();
            return Ok(statusList);
        }

        [HttpGet("device/{deviceId}")]
        public IActionResult GetDeviceData(string deviceId)
        {
            var data = _dataService.GetLatestDeviceData(deviceId);
            if (data == null)
                return NotFound($"Device {deviceId} not found");

            return Ok(data);
        }

        [HttpGet("device/{deviceId}/status")]
        public IActionResult GetDeviceStatus(string deviceId)
        {
            var isOnline = _dataService.IsDeviceOnline(deviceId);
            var lastHeartbeat = _dataService.GetLastHeartbeat(deviceId);
            var latestData = _dataService.GetLatestDeviceData(deviceId);

            var status = new
            {
                DeviceId = deviceId,
                IsOnline = isOnline,
                LastHeartbeat = lastHeartbeat,
                LastUpdate = latestData?.Timestamp,
                ChannelCount = latestData?.Channels?.Count ?? 0,
                ActiveChannels = latestData?.Channels?.Count(c => c.Status != ChannelStatus.Idle) ?? 0,
                TotalPower = latestData?.Channels?.Sum(c => c.Power) ?? 0,
                HasAlarms = latestData?.AlarmData?.Any() ?? false,
                AlarmCount = latestData?.AlarmData?.Count ?? 0,
                HasCriticalAlarms = latestData?.AlarmData?.Any(a => a.Severity == AlarmSeverity.Critical) ?? false
            };

            return Ok(status);
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

        [HttpPost("device/{deviceId}/offline")]
        public async Task<IActionResult> MarkDeviceOffline(string deviceId, [FromBody] OfflineRequest request)
        {
            try
            {
                var reason = request?.Reason ?? "API request";
                await _dataService.MarkDeviceOfflineAsync(deviceId, reason);
                
                return Ok(new { 
                    success = true, 
                    deviceId = deviceId, 
                    reason = reason, 
                    timestamp = DateTime.UtcNow 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking device {deviceId} as offline");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            var health = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                TotalDevices = _dataService.GetAllKnownDevices().Count,
                OnlineDevices = _dataService.GetActiveDevices().Count,
                OfflineDevices = _dataService.GetAllKnownDevices().Count - _dataService.GetActiveDevices().Count
            };

            return Ok(health);
        }

        // Heartbeat endpoint for devices to maintain connection
        [HttpPost("device/{deviceId}/heartbeat")]
        public async Task<IActionResult> DeviceHeartbeat(string deviceId)
        {
            try
            {
                // Create minimal device data for heartbeat
                var heartbeatData = new DeviceData
                {
                    DeviceId = deviceId,
                    Timestamp = DateTime.UtcNow,
                    Channels = new List<ChannelData>(),
                    AuxData = new List<AuxData>(),
                    CANData = new List<CANData>(),
                    LINData = new List<LINData>(),
                    AlarmData = new List<AlarmData>()
                };

                await _dataService.UpdateDeviceDataAsync(heartbeatData);
                
                return Ok(new { 
                    success = true, 
                    deviceId = deviceId, 
                    timestamp = DateTime.UtcNow,
                    message = "Heartbeat received"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing heartbeat for device {deviceId}");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class OfflineRequest
    {
        public string? Reason { get; set; }
    }
}