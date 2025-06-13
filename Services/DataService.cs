// Services/DataService.cs - 메모리 및 성능 최적화 버전
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
        
        // 메모리 효율성을 위한 설정
        private readonly ConcurrentDictionary<string, DeviceData> _latestData = new();
        private readonly ConcurrentDictionary<string, CircularBuffer<DeviceData>> _dataHistory = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();
        private readonly ConcurrentDictionary<string, bool> _deviceOnlineStatus = new();
        
        // 성능 최적화를 위한 설정
        private const int MaxHistoryCount = 500; // 1000에서 500으로 감소
        private const int HeartbeatTimeoutSeconds = 30;
        private const int InactiveDeviceTimeoutMinutes = 5;
        private const int MaxDeviceCount = 100; // 최대 디바이스 수 제한
        
        // 브로드캐스트 최적화를 위한 설정
        private readonly Timer _periodicCleanupTimer;
        private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
        private volatile bool _disposed = false;

        public DataService(IHubContext<DeviceDataHub> hubContext, ILogger<DataService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
            
            // 주기적 정리 타이머 설정 (5분마다)
            _periodicCleanupTimer = new Timer(PerformPeriodicCleanup, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task UpdateDeviceDataAsync(DeviceData deviceData)
        {
            if (_disposed) return;
            
            try
            {
                if (!await ValidateDeviceDataAsync(deviceData))
                {
                    _logger.LogWarning($"Invalid data received from device {deviceData.DeviceId}");
                    return;
                }

                var deviceId = deviceData.DeviceId;
                var now = DateTime.UtcNow;

                // 메모리 제한 체크
                if (_latestData.Count >= MaxDeviceCount && !_latestData.ContainsKey(deviceId))
                {
                    _logger.LogWarning($"Maximum device count ({MaxDeviceCount}) reached. Rejecting new device {deviceId}");
                    return;
                }

                // 최신 데이터 업데이트 (이전 데이터는 GC가 자동 정리)
                _latestData.AddOrUpdate(deviceId, deviceData, (key, oldValue) => deviceData);

                // 하트비트 업데이트
                _lastHeartbeat.AddOrUpdate(deviceId, now, (key, oldValue) => now);

                // 온라인 상태 업데이트
                var wasOnline = _deviceOnlineStatus.GetValueOrDefault(deviceId, false);
                _deviceOnlineStatus.AddOrUpdate(deviceId, true, (key, oldValue) => true);

                // 상태 변경 시에만 브로드캐스트
                if (!wasOnline)
                {
                    _logger.LogInformation($"Device {deviceId} came online");
                    _ = Task.Run(() => BroadcastDeviceStatusChangeAsync(deviceId, true));
                }

                // 효율적인 히스토리 관리 (CircularBuffer 사용)
                var history = _dataHistory.GetOrAdd(deviceId, _ => new CircularBuffer<DeviceData>(MaxHistoryCount));
                history.Add(deviceData);

                // 즉시 브로드캐스트 (비동기로 실행하여 블로킹 방지)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _hubContext.Clients.Group($"Device_{deviceId}")
                            .SendAsync("ReceiveDeviceData", deviceData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to broadcast data for device {deviceId}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating device data for {deviceData.DeviceId}");
            }
        }

        public DeviceData? GetLatestDeviceData(string deviceId)
        {
            return _latestData.TryGetValue(deviceId, out var data) ? data : null;
        }

        public List<DeviceData> GetDeviceDataHistory(string deviceId, int count = 100)
        {
            if (!_dataHistory.TryGetValue(deviceId, out var history))
                return new List<DeviceData>();

            // 요청된 개수만큼만 반환하여 메모리 사용량 최적화
            return history.GetLast(Math.Min(count, MaxHistoryCount));
        }

        public List<string> GetActiveDevices()
        {
            // LINQ 대신 직접 반복으로 성능 최적화
            var activeDevices = new List<string>();
            foreach (var kvp in _deviceOnlineStatus)
            {
                if (kvp.Value)
                    activeDevices.Add(kvp.Key);
            }
            return activeDevices;
        }

        public List<string> GetAllKnownDevices()
        {
            return new List<string>(_latestData.Keys);
        }

        public bool IsDeviceOnline(string deviceId)
        {
            return _deviceOnlineStatus.GetValueOrDefault(deviceId, false);
        }

        public DateTime? GetLastHeartbeat(string deviceId)
        {
            return _lastHeartbeat.TryGetValue(deviceId, out var lastTime) ? lastTime : null;
        }

        public async Task CheckDeviceTimeoutsAsync()
        {
            if (_disposed) return;
            
            var now = DateTime.UtcNow;
            var devicesToMarkOffline = new List<string>();

            // 타임아웃 체크 최적화
            foreach (var kvp in _lastHeartbeat)
            {
                var deviceId = kvp.Key;
                var lastHeartbeat = kvp.Value;
                var isCurrentlyOnline = _deviceOnlineStatus.GetValueOrDefault(deviceId, false);

                if (isCurrentlyOnline && (now - lastHeartbeat).TotalSeconds > HeartbeatTimeoutSeconds)
                {
                    devicesToMarkOffline.Add(deviceId);
                }
            }

            // 배치로 오프라인 처리
            if (devicesToMarkOffline.Any())
            {
                var tasks = devicesToMarkOffline.Select(async deviceId =>
                {
                    _deviceOnlineStatus.AddOrUpdate(deviceId, false, (key, oldValue) => false);
                    _logger.LogWarning($"Device {deviceId} marked as offline due to heartbeat timeout");
                    await BroadcastDeviceStatusChangeAsync(deviceId, false);
                });

                await Task.WhenAll(tasks);
            }

            // 비활성 디바이스 정리 (별도 메서드로 분리)
            await CleanupInactiveDevicesAsync(now);
        }

        private async Task CleanupInactiveDevicesAsync(DateTime now)
        {
            var inactiveDevices = new List<string>();
            
            foreach (var kvp in _lastHeartbeat)
            {
                if ((now - kvp.Value).TotalMinutes > InactiveDeviceTimeoutMinutes)
                {
                    inactiveDevices.Add(kvp.Key);
                }
            }

            foreach (var deviceId in inactiveDevices)
            {
                _logger.LogInformation($"Removing inactive device {deviceId} from memory");
                
                _lastHeartbeat.TryRemove(deviceId, out _);
                _deviceOnlineStatus.TryRemove(deviceId, out _);
                _latestData.TryRemove(deviceId, out _);
                _dataHistory.TryRemove(deviceId, out _);
            }

            await Task.CompletedTask;
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
            await Task.CompletedTask;
            
            if (string.IsNullOrEmpty(deviceData.DeviceId))
            {
                return false;
            }

            // 더 엄격한 데이터 검증으로 메모리 보호
            if (deviceData.Channels.Count > 64) return false; // 128에서 64로 감소
            if (deviceData.AuxData.Count > 128) return false; // 256에서 128로 감소
            if (deviceData.CANData.Count > 128) return false;
            if (deviceData.LINData.Count > 128) return false;
            if (deviceData.AlarmData.Count > 50) return false; // 새로 추가

            return true;
        }

        public async Task BroadcastLatestDataAsync(string deviceId)
        {
            try
            {
                var latestData = GetLatestDeviceData(deviceId);
                if (latestData != null)
                {
                    await _hubContext.Clients.Group($"Device_{deviceId}")
                        .SendAsync("ReceiveDeviceData", latestData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during manual broadcast for device {deviceId}");
            }
        }

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

        public List<object> GetDeviceStatusSummary()
        {
            var statusList = new List<object>();
            var allDevices = GetAllKnownDevices();

            // 병렬 처리로 성능 최적화
            var summaries = allDevices.AsParallel().Select(deviceId =>
            {
                var isOnline = IsDeviceOnline(deviceId);
                var lastHeartbeat = GetLastHeartbeat(deviceId);
                var latestData = GetLatestDeviceData(deviceId);

                return new
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
            }).ToList();

            return summaries.Cast<object>().ToList();
        }

        // 주기적 정리 작업
        private async void PerformPeriodicCleanup(object? state)
        {
            if (_disposed) return;
            
            if (await _cleanupSemaphore.WaitAsync(100))
            {
                try
                {
                    await CheckDeviceTimeoutsAsync();
                    
                    // 메모리 사용량 체크 및 강제 GC
                    var memoryBefore = GC.GetTotalMemory(false);
                    if (memoryBefore > 200 * 1024 * 1024) // 200MB 이상일 때
                    {
                        _logger.LogInformation($"Memory usage high: {memoryBefore / 1024 / 1024} MB, triggering cleanup");
                        
                        // 오래된 히스토리 데이터 정리
                        CleanupOldHistoryData();
                        
                        GC.Collect(2, GCCollectionMode.Optimized);
                        GC.WaitForPendingFinalizers();
                        
                        var memoryAfter = GC.GetTotalMemory(true);
                        _logger.LogInformation($"Memory after cleanup: {memoryAfter / 1024 / 1024} MB");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during periodic cleanup");
                }
                finally
                {
                    _cleanupSemaphore.Release();
                }
            }
        }

        private void CleanupOldHistoryData()
        {
            foreach (var kvp in _dataHistory.ToArray())
            {
                var history = kvp.Value;
                if (history.Count > MaxHistoryCount / 2)
                {
                    // 오래된 데이터의 절반 정도 제거
                    history.TrimToSize(MaxHistoryCount / 2);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _periodicCleanupTimer?.Dispose();
            _cleanupSemaphore?.Dispose();
            
            // 메모리 정리
            _latestData.Clear();
            _dataHistory.Clear();
            _lastHeartbeat.Clear();
            _deviceOnlineStatus.Clear();
        }
    }

    // 메모리 효율적인 순환 버퍼 구현
    public class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        private int _head = 0;
        private int _tail = 0;
        private int _count = 0;
        private readonly object _lock = new object();

        public CircularBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public int Count 
        { 
            get 
            { 
                lock (_lock) 
                { 
                    return _count; 
                } 
            } 
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_tail] = item;
                _tail = (_tail + 1) % _capacity;
                
                if (_count < _capacity)
                {
                    _count++;
                }
                else
                {
                    _head = (_head + 1) % _capacity; // 오래된 데이터 덮어쓰기
                }
            }
        }

        public List<T> GetLast(int count)
        {
            lock (_lock)
            {
                var result = new List<T>(Math.Min(count, _count));
                var actualCount = Math.Min(count, _count);
                
                for (int i = 0; i < actualCount; i++)
                {
                    var index = (_tail - actualCount + i + _capacity) % _capacity;
                    result.Add(_buffer[index]);
                }
                
                return result;
            }
        }

        public void TrimToSize(int newSize)
        {
            lock (_lock)
            {
                if (newSize >= _count) return;
                
                // 최신 데이터만 유지
                var newBuffer = new T[_capacity];
                int sourceIndex = (_tail - newSize + _capacity) % _capacity;
                
                for (int i = 0; i < newSize; i++)
                {
                    newBuffer[i] = _buffer[(sourceIndex + i) % _capacity];
                }
                
                Array.Copy(newBuffer, _buffer, _capacity);
                _head = 0;
                _tail = newSize % _capacity;
                _count = newSize;
            }
        }
    }
}