// Services/DataService.cs
using GPIMSWebServer.Models;
using Microsoft.AspNetCore.SignalR;
using GPIMSWebServer.Hubs;
using System.Collections.Concurrent;

namespace GPIMSWebServer.Services
{
    public class DataService : IDataService
    {
        private readonly IHubContext<DeviceDataHub> _hubContext;
        private readonly ILogger<DataService> _logger;
        private readonly ConcurrentDictionary<string, DeviceData> _latestData = new();
        private readonly ConcurrentDictionary<string, Queue<DeviceData>> _dataHistory = new();
        private const int MaxHistoryCount = 1000;

        public DataService(IHubContext<DeviceDataHub> hubContext, ILogger<DataService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task UpdateDeviceDataAsync(DeviceData deviceData)
        {
            try
            {
                if (!await ValidateDeviceDataAsync(deviceData))
                {
                    _logger.LogWarning($"Invalid data received from device {deviceData.DeviceId}");
                    return;
                }

                // Update latest data
                _latestData.AddOrUpdate(deviceData.DeviceId, deviceData, (key, oldValue) => deviceData);

                // Update history
                var history = _dataHistory.GetOrAdd(deviceData.DeviceId, _ => new Queue<DeviceData>());
                
                lock (history)
                {
                    history.Enqueue(deviceData);
                    while (history.Count > MaxHistoryCount)
                    {
                        history.Dequeue();
                    }
                }

                // 즉시 브로드캐스트 - 데이터가 들어오는 즉시 전송
                try
                {
                    await _hubContext.Clients.Group($"Device_{deviceData.DeviceId}")
                        .SendAsync("ReceiveDeviceData", deviceData);
                    
                    _logger.LogDebug($"Data broadcasted immediately for device {deviceData.DeviceId} with {deviceData.Channels.Count} channels");
                }
                catch (Exception broadcastEx)
                {
                    _logger.LogError(broadcastEx, $"Failed to broadcast data for device {deviceData.DeviceId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating device data for {deviceData.DeviceId}");
            }
        }

        public DeviceData? GetLatestDeviceData(string deviceId)
        {
            _latestData.TryGetValue(deviceId, out var data);
            return data;
        }

        public List<DeviceData> GetDeviceDataHistory(string deviceId, int count = 100)
        {
            if (!_dataHistory.TryGetValue(deviceId, out var history))
                return new List<DeviceData>();

            lock (history)
            {
                return history.TakeLast(count).ToList();
            }
        }

        public List<string> GetActiveDevices()
        {
            // 최근 5분 이내에 데이터가 있는 디바이스만 활성으로 간주
            var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
            return _latestData
                .Where(kvp => kvp.Value.Timestamp > cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        public async Task<bool> ValidateDeviceDataAsync(DeviceData deviceData)
        {
            await Task.CompletedTask; // Placeholder for async validation if needed
            
            if (string.IsNullOrEmpty(deviceData.DeviceId))
            {
                _logger.LogWarning("Device data validation failed: DeviceId is null or empty");
                return false;
            }

            if (deviceData.Channels.Count > 128)
            {
                _logger.LogWarning($"Device data validation failed: Too many channels ({deviceData.Channels.Count})");
                return false;
            }

            if (deviceData.AuxData.Count > 256)
            {
                _logger.LogWarning($"Device data validation failed: Too many aux data points ({deviceData.AuxData.Count})");
                return false;
            }

            if (deviceData.CANData.Count > 256)
            {
                _logger.LogWarning($"Device data validation failed: Too many CAN data points ({deviceData.CANData.Count})");
                return false;
            }

            if (deviceData.LINData.Count > 256)
            {
                _logger.LogWarning($"Device data validation failed: Too many LIN data points ({deviceData.LINData.Count})");
                return false;
            }

            return true;
        }

        // 추가: 특정 디바이스의 최신 데이터를 강제로 브로드캐스트
        public async Task BroadcastLatestDataAsync(string deviceId)
        {
            try
            {
                var latestData = GetLatestDeviceData(deviceId);
                if (latestData != null)
                {
                    await _hubContext.Clients.Group($"Device_{deviceId}")
                        .SendAsync("ReceiveDeviceData", latestData);
                    
                    _logger.LogDebug($"Manual broadcast completed for device {deviceId}");
                }
                else
                {
                    _logger.LogWarning($"No data available for manual broadcast of device {deviceId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during manual broadcast for device {deviceId}");
            }
        }
    }
}