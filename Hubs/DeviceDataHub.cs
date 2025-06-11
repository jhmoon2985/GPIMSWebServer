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
                
                // 그룹 참가 즉시 최신 데이터 및 상태 전송
                await RequestLatestData(deviceId);
                await SendDeviceStatus(deviceId);
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
                var allDevices = _dataService.GetAllKnownDevices();
                var deviceListWithStatus = allDevices.Select(deviceId => new
                {
                    DeviceId = deviceId,
                    IsOnline = _dataService.IsDeviceOnline(deviceId),
                    LastHeartbeat = _dataService.GetLastHeartbeat(deviceId)
                }).ToList();

                await Clients.Caller.SendAsync("ReceiveDeviceList", deviceListWithStatus);
                _logger.LogDebug($"Device list with status sent to caller: {allDevices.Count} devices");
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

        // 새로 추가: 특정 디바이스 상태 전송
        public async Task SendDeviceStatus(string deviceId)
        {
            try
            {
                var isOnline = _dataService.IsDeviceOnline(deviceId);
                var lastHeartbeat = _dataService.GetLastHeartbeat(deviceId);
                var latestData = _dataService.GetLatestDeviceData(deviceId);

                var deviceStatus = new
                {
                    DeviceId = deviceId,
                    IsOnline = isOnline,
                    LastHeartbeat = lastHeartbeat,
                    LastUpdate = latestData?.Timestamp,
                    ChannelCount = latestData?.Channels?.Count ?? 0,
                    ActiveChannels = latestData?.Channels?.Count(c => c.Status != ChannelStatus.Idle) ?? 0,
                    TotalPower = latestData?.Channels?.Sum(c => c.Power) ?? 0,
                    HasAlarms = latestData?.AlarmData?.Any() ?? false
                };

                await Clients.Caller.SendAsync("ReceiveDeviceStatus", deviceStatus);
                _logger.LogDebug($"Device status sent for {deviceId}: {(isOnline ? "Online" : "Offline")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending device status for {deviceId}");
            }
        }

        // 새로 추가: 디바이스를 수동으로 오프라인으로 표시
        public async Task MarkDeviceOffline(string deviceId, string reason = "Manual disconnect")
        {
            try
            {
                await _dataService.MarkDeviceOfflineAsync(deviceId, reason);
                _logger.LogInformation($"Device {deviceId} manually marked offline by connection {Context.ConnectionId}. Reason: {reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error manually marking device {deviceId} offline");
            }
        }

        // 새로 추가: 여러 디바이스 그룹에 한번에 참가
        public async Task JoinMultipleDeviceGroups(List<string> deviceIds)
        {
            try
            {
                foreach (var deviceId in deviceIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                    _logger.LogInformation($"Client {Context.ConnectionId} joined group Device_{deviceId}");
                }
                
                // 모든 디바이스의 최신 데이터 및 상태 전송
                foreach (var deviceId in deviceIds)
                {
                    var latestData = _dataService.GetLatestDeviceData(deviceId);
                    if (latestData != null)
                    {
                        await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
                    }
                    await SendDeviceStatus(deviceId);
                }
                
                _logger.LogInformation($"Client {Context.ConnectionId} joined {deviceIds.Count} device groups");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining multiple device groups for connection {Context.ConnectionId}");
            }
        }

        // 새로 추가: 전체 디바이스 상태 브로드캐스트
        public async Task BroadcastAllDevicesStatus()
        {
            try
            {
                var deviceStatusList = _dataService.GetDeviceStatusSummary();
                await Clients.All.SendAsync("ReceiveAllDevicesStatus", deviceStatusList);
                _logger.LogDebug($"Broadcasted status for {deviceStatusList.Count} devices");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting all devices status");
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
                _logger.LogInformation($"Client {Context.ConnectionId} connected to DeviceDataHub");
                
                // 연결 즉시 활성 디바이스 목록 전송
                await RequestDeviceList();
                
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