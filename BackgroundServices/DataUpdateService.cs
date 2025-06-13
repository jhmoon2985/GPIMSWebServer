// BackgroundServices/DataUpdateService.cs - 성능 최적화 버전
using GPIMSWebServer.Services;
using GPIMSWebServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace GPIMSWebServer.BackgroundServices
{
    public class DataUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataUpdateService> _logger;
        
        // 최적화된 인터벌 설정
        private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(1000); // 0.5초에서 1초로 증가
        private readonly TimeSpan _statusBroadcastInterval = TimeSpan.FromSeconds(30); // 10초에서 30초로 증가
        private readonly TimeSpan _timeoutCheckInterval = TimeSpan.FromSeconds(10); // 5초에서 10초로 증가
        private readonly TimeSpan _maintenanceInterval = TimeSpan.FromMinutes(5); // 유지보수 작업 주기
        
        private DateTime _lastStatusBroadcast = DateTime.MinValue;
        private DateTime _lastTimeoutCheck = DateTime.MinValue;
        private DateTime _lastMaintenance = DateTime.MinValue;
        
        // 성능 모니터링
        private long _totalUpdates = 0;
        private long _totalBroadcasts = 0;
        private readonly object _statsLock = new object();

        public DataUpdateService(IServiceProvider serviceProvider, ILogger<DataUpdateService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data Update Service started with optimized {UpdateInterval}ms interval", 
                _updateInterval.TotalMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    
                    // 서비스 스코프 생성 최적화
                    using var scope = _serviceProvider.CreateScope();
                    var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
                    
                    // 비동기 작업들을 병렬로 처리
                    var tasks = new List<Task>();
                    
                    // 1. 디바이스 타임아웃 체크 (주기적)
                    if (now - _lastTimeoutCheck > _timeoutCheckInterval)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await dataService.CheckDeviceTimeoutsAsync();
                                _lastTimeoutCheck = now;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error during timeout check");
                            }
                        }));
                    }
                    
                    // 2. 디바이스 상태 브로드캐스트 (주기적)
                    if (now - _lastStatusBroadcast > _statusBroadcastInterval)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DeviceDataHub>>();
                                await BroadcastDeviceStatusSummary(hubContext, dataService);
                                _lastStatusBroadcast = now;
                                
                                lock (_statsLock)
                                {
                                    _totalBroadcasts++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error during status broadcast");
                            }
                        }));
                    }
                    
                    // 3. 유지보수 작업 (주기적)
                    if (now - _lastMaintenance > _maintenanceInterval)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await PerformMaintenanceTasks(dataService);
                                _lastMaintenance = now;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error during maintenance");
                            }
                        }));
                    }
                    
                    // 4. 활성 디바이스 데이터 업데이트 (최적화된 방식)
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessActiveDevices(dataService, scope.ServiceProvider);
                            
                            lock (_statsLock)
                            {
                                _totalUpdates++;
                                
                                // 통계 로깅 (1000회마다)
                                if (_totalUpdates % 1000 == 0)
                                {
                                    _logger.LogInformation($"Service stats: {_totalUpdates} updates, {_totalBroadcasts} broadcasts processed");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing active devices");
                        }
                    }));
                    
                    // 모든 작업이 완료될 때까지 대기 (하지만 너무 오래 걸리면 타임아웃)
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                    
                    try
                    {
                        await Task.WhenAll(tasks.Where(t => t != null));
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                    {
                        _logger.LogWarning("Background tasks timed out, continuing with next cycle");
                    }
                    
                    // 적응형 대기 시간 (시스템 부하에 따라 조정)
                    var delayTime = CalculateOptimalDelay();
                    await Task.Delay(delayTime, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in data update service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

            _logger.LogInformation("Data Update Service stopped. Total updates: {TotalUpdates}, broadcasts: {TotalBroadcasts}", 
                _totalUpdates, _totalBroadcasts);
        }

        private async Task ProcessActiveDevices(IDataService dataService, IServiceProvider serviceProvider)
        {
            var activeDevices = dataService.GetActiveDevices();
            
            if (!activeDevices.Any())
            {
                return;
            }

            var hubContext = serviceProvider.GetRequiredService<IHubContext<DeviceDataHub>>();
            var broadcastTasks = new List<Task>();
            
            // 배치 처리로 성능 최적화
            const int batchSize = 10;
            for (int i = 0; i < activeDevices.Count; i += batchSize)
            {
                var batch = activeDevices.Skip(i).Take(batchSize);
                
                var batchTask = Task.Run(async () =>
                {
                    foreach (var deviceId in batch)
                    {
                        try
                        {
                            var latestData = dataService.GetLatestDeviceData(deviceId);
                            if (latestData != null)
                            {
                                await hubContext.Clients.Group($"Device_{deviceId}")
                                    .SendAsync("ReceiveDeviceData", latestData);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to broadcast data for device {deviceId}");
                        }
                    }
                });
                
                broadcastTasks.Add(batchTask);
            }
            
            // 모든 배치 작업 완료 대기 (타임아웃 포함)
            try
            {
                await Task.WhenAll(broadcastTasks).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Device broadcast tasks timed out");
            }
        }

        private async Task BroadcastDeviceStatusSummary(IHubContext<DeviceDataHub> hubContext, IDataService dataService)
        {
            try
            {
                var deviceStatusList = dataService.GetDeviceStatusSummary();
                
                if (deviceStatusList.Any())
                {
                    await hubContext.Clients.All.SendAsync("ReceiveDeviceStatusSummary", deviceStatusList);
                    
                    var onlineCount = deviceStatusList.Count(d => ((dynamic)d).IsOnline);
                    var offlineCount = deviceStatusList.Count - onlineCount;
                    
                    _logger.LogDebug($"Broadcasted optimized status summary: {onlineCount} online, {offlineCount} offline devices");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error broadcasting device status summary");
            }
        }

        private async Task PerformMaintenanceTasks(IDataService dataService)
        {
            try
            {
                var activeDevices = dataService.GetActiveDevices();
                var allDevices = dataService.GetAllKnownDevices();
                var offlineDevices = allDevices.Except(activeDevices).ToList();
                
                // 메모리 사용량 모니터링
                var memoryBefore = GC.GetTotalMemory(false);
                var memoryMB = memoryBefore / 1024 / 1024;
                
                if (memoryMB > 150) // 150MB 이상일 때 로그
                {
                    _logger.LogInformation($"High memory usage detected: {memoryMB} MB");
                    
                    // 메모리 압박 시 강제 GC 수행
                    if (memoryMB > 300)
                    {
                        _logger.LogWarning($"Critical memory usage: {memoryMB} MB, forcing garbage collection");
                        GC.Collect(2, GCCollectionMode.Forced);
                        GC.WaitForPendingFinalizers();
                        
                        var memoryAfter = GC.GetTotalMemory(true);
                        _logger.LogInformation($"Memory after forced GC: {memoryAfter / 1024 / 1024} MB");
                    }
                }
                
                // 디바이스 상태 요약 로깅 (레벨 조정)
                if (activeDevices.Count > 0 || offlineDevices.Count > 0)
                {
                    if (activeDevices.Count > 20) // 많은 디바이스가 있을 때만 요약 로그
                    {
                        _logger.LogInformation($"Device summary: {activeDevices.Count} online, {offlineDevices.Count} offline");
                    }
                    else
                    {
                        _logger.LogDebug($"Device status: {activeDevices.Count} online ({string.Join(", ", activeDevices.Take(10))}), " +
                                        $"{offlineDevices.Count} offline");
                    }
                }
                
                // 성능 통계 로깅
                lock (_statsLock)
                {
                    if (_totalUpdates % 5000 == 0 && _totalUpdates > 0)
                    {
                        var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
                        var avgUpdatesPerMin = _totalUpdates / Math.Max(1, uptime.TotalMinutes);
                        
                        _logger.LogInformation($"Performance stats: {avgUpdatesPerMin:F1} updates/min, Memory: {memoryMB} MB, Uptime: {uptime:hh\\:mm\\:ss}");
                    }
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during maintenance tasks");
            }
        }

        private TimeSpan CalculateOptimalDelay()
        {
            // 현재 메모리 사용량에 따라 적응형 지연 시간 계산
            var currentMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
            
            if (currentMemory > 200)
            {
                // 메모리 사용량이 높으면 더 긴 지연
                return TimeSpan.FromMilliseconds(2000);
            }
            else if (currentMemory > 100)
            {
                return TimeSpan.FromMilliseconds(1500);
            }
            else
            {
                return _updateInterval; // 기본 1초
            }
        }
        
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data Update Service is stopping...");
            
            // 통계 출력
            lock (_statsLock)
            {
                _logger.LogInformation($"Final stats - Total updates: {_totalUpdates}, Total broadcasts: {_totalBroadcasts}");
            }
            
            await base.StopAsync(stoppingToken);
        }
    }
}