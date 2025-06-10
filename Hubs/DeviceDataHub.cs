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
                
                // 모든 디바이스의 최신 데이터 전송
                foreach (var deviceId in deviceIds)
                {
                    var latestData = _dataService.GetLatestDeviceData(deviceId);
                    if (latestData != null)
                    {
                        await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
                    }
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
                var activeDevices = _dataService.GetActiveDevices();
                var deviceStatusList = new List<object>();
                
                foreach (var deviceId in activeDevices)
                {
                    var latestData = _dataService.GetLatestDeviceData(deviceId);
                    if (latestData != null)
                    {
                        var deviceStatus = new
                        {
                            DeviceId = deviceId,
                            IsOnline = true,
                            LastUpdate = latestData.Timestamp,
                            ChannelCount = latestData.Channels.Count,
                            ActiveChannels = latestData.Channels.Count(c => c.Status != ChannelStatus.Idle),
                            TotalPower = latestData.Channels.Sum(c => c.Power),
                            HasAlarms = latestData.AlarmData.Any()
                        };
                        deviceStatusList.Add(deviceStatus);
                    }
                }
                
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