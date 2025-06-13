// Hubs/DeviceDataHub.cs - 메모리 및 성능 최적화 버전
using Microsoft.AspNetCore.SignalR;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;
using System.Collections.Concurrent;

namespace GPIMSWebServer.Hubs
{
    public class DeviceDataHub : Hub
    {
        private readonly IDataService _dataService;
        private readonly ILogger<DeviceDataHub> _logger;
        
        // 연결 관리 최적화
        private static readonly ConcurrentDictionary<string, HashSet<string>> _deviceGroups = new();
        private static readonly ConcurrentDictionary<string, DateTime> _connectionLastActivity = new();
        private static readonly ConcurrentDictionary<string, string> _connectionInfo = new();
        private static readonly object _groupsLock = new object();
        
        // 성능 모니터링
        private static long _totalConnections = 0;
        private static long _totalDisconnections = 0;
        private static long _totalGroupJoins = 0;

        public DeviceDataHub(IDataService dataService, ILogger<DeviceDataHub> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async Task JoinDeviceGroup(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId.Length > 50)
            {
                _logger.LogWarning($"Invalid device ID for connection {Context.ConnectionId}");
                return;
            }

            try
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                
                // 연결 추적 최적화
                lock (_groupsLock)
                {
                    if (!_deviceGroups.ContainsKey(deviceId))
                    {
                        _deviceGroups[deviceId] = new HashSet<string>();
                    }
                    _deviceGroups[deviceId].Add(Context.ConnectionId);
                }
                
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                Interlocked.Increment(ref _totalGroupJoins);
                
                _logger.LogDebug($"Client {Context.ConnectionId} joined group Device_{deviceId}");
                
                // 비동기로 최신 데이터 전송 (블로킹 방지)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RequestLatestData(deviceId);
                        await SendDeviceStatus(deviceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error sending initial data for device {deviceId}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining device group {deviceId} for connection {Context.ConnectionId}");
            }
        }

        public async Task LeaveDeviceGroup(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                
                // 연결 추적에서 제거
                lock (_groupsLock)
                {
                    if (_deviceGroups.TryGetValue(deviceId, out var connections))
                    {
                        connections.Remove(Context.ConnectionId);
                        
                        // 빈 그룹 정리
                        if (connections.Count == 0)
                        {
                            _deviceGroups.TryRemove(deviceId, out _);
                        }
                    }
                }
                
                _logger.LogDebug($"Client {Context.ConnectionId} left group Device_{deviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error leaving device group {deviceId} for connection {Context.ConnectionId}");
            }
        }

        public async Task RequestLatestData(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            try
            {
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                var latestData = _dataService.GetLatestDeviceData(deviceId);
                if (latestData != null)
                {
                    await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
                }
                else
                {
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
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                // 병렬로 디바이스 정보 수집
                var allDevices = _dataService.GetAllKnownDevices();
                var deviceListWithStatus = await Task.Run(() =>
                {
                    return allDevices.AsParallel().Select(deviceId => new
                    {
                        DeviceId = deviceId,
                        IsOnline = _dataService.IsDeviceOnline(deviceId),
                        LastHeartbeat = _dataService.GetLastHeartbeat(deviceId)
                    }).ToList();
                });

                await Clients.Caller.SendAsync("ReceiveDeviceList", deviceListWithStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending device list to connection {Context.ConnectionId}");
            }
        }

        public async Task RefreshDeviceData(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            try
            {
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                // 비동기로 처리하여 응답성 향상
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _dataService.BroadcastLatestDataAsync(deviceId);
                        _logger.LogDebug($"Manual refresh triggered for device {deviceId} by connection {Context.ConnectionId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error during manual refresh for device {deviceId}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refresh request for device {deviceId}");
            }
        }

        public async Task SendDeviceStatus(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending device status for {deviceId}");
            }
        }

        public async Task MarkDeviceOffline(string deviceId, string reason = "Manual disconnect")
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            try
            {
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                await _dataService.MarkDeviceOfflineAsync(deviceId, reason);
                _logger.LogInformation($"Device {deviceId} manually marked offline by connection {Context.ConnectionId}. Reason: {reason}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error manually marking device {deviceId} offline");
            }
        }

        public async Task JoinMultipleDeviceGroups(List<string> deviceIds)
        {
            if (deviceIds == null || !deviceIds.Any() || deviceIds.Count > 50) // 최대 50개 제한
            {
                _logger.LogWarning($"Invalid device IDs list for connection {Context.ConnectionId}");
                return;
            }

            try
            {
                var tasks = deviceIds.Select(async deviceId =>
                {
                    if (!string.IsNullOrEmpty(deviceId) && deviceId.Length <= 50)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
                        
                        lock (_groupsLock)
                        {
                            if (!_deviceGroups.ContainsKey(deviceId))
                            {
                                _deviceGroups[deviceId] = new HashSet<string>();
                            }
                            _deviceGroups[deviceId].Add(Context.ConnectionId);
                        }
                    }
                });
                
                await Task.WhenAll(tasks);
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                _logger.LogDebug($"Client {Context.ConnectionId} joined {deviceIds.Count} device groups");
                
                // 배치로 데이터 전송 (성능 최적화)
                _ = Task.Run(async () =>
                {
                    var dataTasks = deviceIds.Where(id => !string.IsNullOrEmpty(id))
                                            .Select(async deviceId =>
                    {
                        try
                        {
                            var latestData = _dataService.GetLatestDeviceData(deviceId);
                            if (latestData != null)
                            {
                                await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
                            }
                            await SendDeviceStatus(deviceId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Error sending data for device {deviceId}");
                        }
                    });
                    
                    await Task.WhenAll(dataTasks);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error joining multiple device groups for connection {Context.ConnectionId}");
            }
        }

        public async Task BroadcastAllDevicesStatus()
        {
            try
            {
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                
                // 비동기로 처리하여 성능 향상
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var deviceStatusList = _dataService.GetDeviceStatusSummary();
                        await Clients.All.SendAsync("ReceiveAllDevicesStatus", deviceStatusList);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error broadcasting all devices status");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing broadcast all devices status request");
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                Interlocked.Increment(ref _totalConnections);
                _connectionLastActivity[Context.ConnectionId] = DateTime.UtcNow;
                _connectionInfo[Context.ConnectionId] = $"{Context.UserIdentifier ?? "Anonymous"}@{Context.GetHttpContext()?.Connection.RemoteIpAddress}";
                
                await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
                _logger.LogDebug($"Client {Context.ConnectionId} connected to DeviceDataHub (Total: {_totalConnections})");
                
                // 비동기로 디바이스 목록 전송
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RequestDeviceList();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error sending initial device list to {Context.ConnectionId}");
                    }
                });
                
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
                Interlocked.Increment(ref _totalDisconnections);
                
                // 연결 정보 정리
                _connectionLastActivity.TryRemove(Context.ConnectionId, out _);
                _connectionInfo.TryRemove(Context.ConnectionId, out _);
                
                // 모든 디바이스 그룹에서 제거
                lock (_groupsLock)
                {
                    var groupsToUpdate = _deviceGroups.Where(kvp => kvp.Value.Contains(Context.ConnectionId)).ToList();
                    
                    foreach (var kvp in groupsToUpdate)
                    {
                        kvp.Value.Remove(Context.ConnectionId);
                        
                        // 빈 그룹 정리
                        if (kvp.Value.Count == 0)
                        {
                            _deviceGroups.TryRemove(kvp.Key, out _);
                        }
                    }
                }
                
                var disconnectReason = exception?.GetType().Name ?? "Normal";
                _logger.LogDebug($"Client {Context.ConnectionId} disconnected from DeviceDataHub. Reason: {disconnectReason}");
                
                if (exception != null)
                {
                    _logger.LogWarning(exception, $"Client {Context.ConnectionId} disconnected with exception");
                }
                
                // 주기적으로 연결 통계 로깅 (1000번째 연결마다)
                if (_totalConnections % 1000 == 0)
                {
                    _logger.LogInformation($"SignalR stats - Total connections: {_totalConnections}, disconnections: {_totalDisconnections}, active groups: {_deviceGroups.Count}");
                }
                
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during client disconnection {Context.ConnectionId}");
            }
        }

        // 정적 메서드로 허브 상태 모니터링
        public static HubConnectionStats GetConnectionStats()
        {
            lock (_groupsLock)
            {
                return new HubConnectionStats
                {
                    TotalConnections = _totalConnections,
                    TotalDisconnections = _totalDisconnections,
                    TotalGroupJoins = _totalGroupJoins,
                    ActiveConnections = _connectionLastActivity.Count,
                    ActiveGroups = _deviceGroups.Count,
                    ConnectionsPerGroup = _deviceGroups.ToDictionary(
                        kvp => kvp.Key, 
                        kvp => kvp.Value.Count
                    )
                };
            }
        }

        // 비활성 연결 정리 (백그라운드 서비스에서 호출)
        public static void CleanupInactiveConnections(TimeSpan inactivityThreshold)
        {
            var cutoffTime = DateTime.UtcNow.Subtract(inactivityThreshold);
            var inactiveConnections = _connectionLastActivity
                .Where(kvp => kvp.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connectionId in inactiveConnections)
            {
                _connectionLastActivity.TryRemove(connectionId, out _);
                _connectionInfo.TryRemove(connectionId, out _);
                
                lock (_groupsLock)
                {
                    var groupsToUpdate = _deviceGroups.Where(kvp => kvp.Value.Contains(connectionId)).ToList();
                    
                    foreach (var kvp in groupsToUpdate)
                    {
                        kvp.Value.Remove(connectionId);
                        
                        if (kvp.Value.Count == 0)
                        {
                            _deviceGroups.TryRemove(kvp.Key, out _);
                        }
                    }
                }
            }
        }
    }

    // 연결 통계 클래스
    public class HubConnectionStats
    {
        public long TotalConnections { get; set; }
        public long TotalDisconnections { get; set; }
        public long TotalGroupJoins { get; set; }
        public int ActiveConnections { get; set; }
        public int ActiveGroups { get; set; }
        public Dictionary<string, int> ConnectionsPerGroup { get; set; } = new();
    }
}