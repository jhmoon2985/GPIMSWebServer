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

                // Broadcast to clients
                await _hubContext.Clients.Group($"Device_{deviceData.DeviceId}")
                    .SendAsync("ReceiveDeviceData", deviceData);

                _logger.LogDebug($"Data updated for device {deviceData.DeviceId} with {deviceData.Channels.Count} channels");
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
            return _latestData.Keys.ToList();
        }

        public async Task<bool> ValidateDeviceDataAsync(DeviceData deviceData)
        {
            await Task.CompletedTask; // Placeholder for async validation if needed
            
            if (string.IsNullOrEmpty(deviceData.DeviceId))
                return false;

            if (deviceData.Channels.Count > 128)
                return false;

            if (deviceData.AuxData.Count > 256)
                return false;

            if (deviceData.CANData.Count > 256)
                return false;

            if (deviceData.LINData.Count > 256)
                return false;

            return true;
        }
    }
}