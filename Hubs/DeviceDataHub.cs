// Hubs/DeviceDataHub.cs
using Microsoft.AspNetCore.SignalR;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;

namespace GPIMSWebServer.Hubs
{
    public class DeviceDataHub : Hub
    {
        private readonly IDataService _dataService;
        private readonly ILogger<DeviceDataHub> _logger;

        public DeviceDataHub(IDataService dataService, ILogger<DeviceDataHub> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async Task JoinDeviceGroup(string deviceId)
        {
            try
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                _logger.LogInformation($"Client {Context.ConnectionId} joined group Device_{deviceId}");
                
                // 그룹 참가 즉시 최신 데이터 전송
                await RequestLatestData(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining device group {deviceId} for connection {Context.ConnectionId}");
            }
        }

        public async Task LeaveDeviceGroup(string deviceId)
        {
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                _logger.LogInformation($"Client {Context.ConnectionId} left group Device_{deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error leaving device group {deviceId} for connection {Context.ConnectionId}");
            }
        }

        public async Task RequestLatestData(string deviceId)
        {
            try
            {
                var latestData = _dataService.GetLatestDeviceData(deviceId);
                if (latestData != null)
                {
                    await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
                    _logger.LogDebug($"Latest data sent to caller for device {deviceId}");
                }
                else
                {
                    _logger.LogWarning($"No latest data available for device {deviceId}");
                    await Clients.Caller.SendAsync("NoDataAvailable", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending latest data for device {deviceId} to connection {Context.ConnectionId}");
            }
        }

        public async Task RequestDeviceList()
        {
            try
            {
                var activeDevices = _dataService.GetActiveDevices();
                await Clients.Caller.SendAsync("ReceiveDeviceList", activeDevices);
                _logger.LogDebug($"Device list sent to caller: {string.Join(", ", activeDevices)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending device list to connection {Context.ConnectionId}");
            }
        }

        public async Task RefreshDeviceData(string deviceId)
        {
            try
            {
                // 강제로 최신 데이터를 브로드캐스트
                await _dataService.BroadcastLatestDataAsync(deviceId);
                _logger.LogInformation($"Manual refresh triggered for device {deviceId} by connection {Context.ConnectionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during manual refresh for device {deviceId}");
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
                _logger.LogInformation($"Client {Context.ConnectionId} connected to DeviceDataHub");
                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during client connection {Context.ConnectionId}");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                _logger.LogInformation($"Client {Context.ConnectionId} disconnected from DeviceDataHub");
                if (exception != null)
                {
                    _logger.LogWarning(exception, $"Client {Context.ConnectionId} disconnected with exception");
                }
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during client disconnection {Context.ConnectionId}");
            }
        }
    }
}