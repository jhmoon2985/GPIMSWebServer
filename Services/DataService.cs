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
        private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new(); // 새로 추가
        private readonly ConcurrentDictionary<string, bool> _deviceOnlineStatus = new(); // 새로 추가
        private const int MaxHistoryCount = 1000;
        private const int HeartbeatTimeoutSeconds = 30; // 30초 이후 오프라인으로 간주
        private const int InactiveDeviceTimeoutMinutes = 5; // 5분 이후 비활성으로 간주

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

                var deviceId = deviceData.DeviceId;
                var now = DateTime.UtcNow;

                // Update latest data
                _latestData.AddOrUpdate(deviceId, deviceData, (key, oldValue) => deviceData);

                // Update heartbeat
                _lastHeartbeat.AddOrUpdate(deviceId, now, (key, oldValue) => now);

                // Update online status if changed
                var wasOnline = _deviceOnlineStatus.GetValueOrDefault(deviceId, false);
                _deviceOnlineStatus.AddOrUpdate(deviceId, true, (key, oldValue) => true);

                // Broadcast device status change if it went from offline to online
                if (!wasOnline)
                {
                    _logger.LogInformation($"Device {deviceId} came online");
                    await BroadcastDeviceStatusChangeAsync(deviceId, true);
                }

                // Update history
                var history = _dataHistory.GetOrAdd(deviceId, _ => new Queue<DeviceData>());
                
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
                    await _hubContext.Clients.Group($"Device_{deviceId}")
                        .SendAsync("ReceiveDeviceData", deviceData);
                    
                    _logger.LogDebug($"Data broadcasted immediately for device {deviceId} with {deviceData.Channels.Count} channels");
                }
                catch (Exception broadcastEx)
                {
                    _logger.LogError(broadcastEx, $"Failed to broadcast data for device {deviceId}");
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
            // 온라인 상태인 디바이스들만 반환
            return _deviceOnlineStatus
                .Where(kvp => kvp.Value) // 온라인 상태인 것만
                .Select(kvp => kvp.Key)
                .ToList();
        }

        public List<string> GetAllKnownDevices()
        {
            // 모든 알려진 디바이스 반환 (온라인/오프라인 구분 없이)
            return _latestData.Keys.ToList();
        }

        public bool IsDeviceOnline(string deviceId)
        {
            return _deviceOnlineStatus.GetValueOrDefault(deviceId, false);
        }

        public DateTime? GetLastHeartbeat(string deviceId)
        {
            return _lastHeartbeat.TryGetValue(deviceId, out var lastTime) ? lastTime : null;
        }

        // 새로운 메서드: 주기적으로 오프라인 디바이스 체크
        public async Task CheckDeviceTimeoutsAsync()
        {
            var now = DateTime.UtcNow;
            var devicesToMarkOffline = new List<string>();

            foreach (var kvp in _lastHeartbeat.ToArray())
            {
                var deviceId = kvp.Key;
                var lastHeartbeat = kvp.Value;
                var isCurrentlyOnline = _deviceOnlineStatus.GetValueOrDefault(deviceId, false);

                // 하트비트 타임아웃 체크 (30초)
                if (isCurrentlyOnline && (now - lastHeartbeat).TotalSeconds > HeartbeatTimeoutSeconds)
                {
                    devicesToMarkOffline.Add(deviceId);
                }
            }

            // 오프라인으로 표시할 디바이스들 처리
            foreach (var deviceId in devicesToMarkOffline)
            {
                _deviceOnlineStatus.AddOrUpdate(deviceId, false, (key, oldValue) => false);
                _logger.LogWarning($"Device {deviceId} marked as offline due to heartbeat timeout");
                
                await BroadcastDeviceStatusChangeAsync(deviceId, false);
            }

            // 비활성 디바이스 정리 (5분 이상 데이터 없음)
            var inactiveDevices = _lastHeartbeat
                .Where(kvp => (now - kvp.Value).TotalMinutes > InactiveDeviceTimeoutMinutes)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var deviceId in inactiveDevices)
            {
                _logger.LogInformation($"Removing inactive device {deviceId} from memory");
                _lastHeartbeat.TryRemove(deviceId, out _);
                _deviceOnlineStatus.TryRemove(deviceId, out _);
                _latestData.TryRemove(deviceId, out _);
                
                if (_dataHistory.TryRemove(deviceId, out _))
                {
                    _logger.LogDebug($"Removed history data for inactive device {deviceId}");
                }
            }
        }

        private async Task BroadcastDeviceStatusChangeAsync(string deviceId, bool isOnline)
        {
            try
            {
                var statusMessage = new
                {
                    DeviceId = deviceId,
                    IsOnline = isOnline,
                    Timestamp = DateTime.UtcNow,
                    LastHeartbeat = GetLastHeartbeat(deviceId)
                };

                // 모든 클라이언트에게 디바이스 상태 변경 알림
                await _hubContext.Clients.All.SendAsync("DeviceStatusChanged", statusMessage);
                
                _logger.LogInformation($"Broadcasted status change for device {deviceId}: {(isOnline ? "Online" : "Offline")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to broadcast status change for device {deviceId}");
            }
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

        // 새로운 메서드: 디바이스를 수동으로 오프라인으로 표시
        public async Task MarkDeviceOfflineAsync(string deviceId, string reason = "Manual")
        {
            try
            {
                var wasOnline = _deviceOnlineStatus.GetValueOrDefault(deviceId, false);
                _deviceOnlineStatus.AddOrUpdate(deviceId, false, (key, oldValue) => false);
                
                if (wasOnline)
                {
                    _logger.LogInformation($"Device {deviceId} manually marked as offline. Reason: {reason}");
                    await BroadcastDeviceStatusChangeAsync(deviceId, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking device {deviceId} as offline");
            }
        }

        // 새로운 메서드: 모든 디바이스 상태 요약 가져오기
        public List<object> GetDeviceStatusSummary()
        {
            var now = DateTime.UtcNow;
            var statusList = new List<object>();

            foreach (var deviceId in GetAllKnownDevices())
            {
                var isOnline = IsDeviceOnline(deviceId);
                var lastHeartbeat = GetLastHeartbeat(deviceId);
                var latestData = GetLatestDeviceData(deviceId);

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

                statusList.Add(status);
            }

            return statusList;
        }
    }
}