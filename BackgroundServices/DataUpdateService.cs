using GPIMSWebServer.Services;
using GPIMSWebServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GPIMSWebServer.BackgroundServices
{
    public class DataUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataUpdateService> _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(500); // 0.5초마다 체크
        private readonly TimeSpan _statusBroadcastInterval = TimeSpan.FromSeconds(10); // 10초마다 전체 상태 브로드캐스트
        private DateTime _lastStatusBroadcast = DateTime.MinValue;

        public DataUpdateService(IServiceProvider serviceProvider, ILogger<DataUpdateService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data Update Service started with {UpdateInterval}ms interval", _updateInterval.TotalMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DeviceDataHub>>();
                    
                    // 활성 디바이스 목록 가져오기
                    var activeDevices = dataService.GetActiveDevices();
                    
                    if (activeDevices.Any())
                    {
                        foreach (var deviceId in activeDevices)
                        {
                            var latestData = dataService.GetLatestDeviceData(deviceId);
                            if (latestData != null)
                            {
                                // 각 디바이스의 최신 데이터를 해당 그룹에 브로드캐스트
                                await hubContext.Clients.Group($"Device_{deviceId}")
                                    .SendAsync("ReceiveDeviceData", latestData, stoppingToken);
                                
                                _logger.LogDebug($"Broadcasted data for device {deviceId} with {latestData.Channels.Count} channels");
                            }
                        }
                        
                        _logger.LogDebug($"Updated {activeDevices.Count} active devices");
                    }
                    else
                    {
                        _logger.LogDebug("No active devices found");
                    }
                    
                    // 주기적으로 전체 디바이스 상태 브로드캐스트
                    if (DateTime.UtcNow - _lastStatusBroadcast > _statusBroadcastInterval)
                    {
                        await BroadcastDeviceStatusSummary(hubContext, dataService, activeDevices);
                        _lastStatusBroadcast = DateTime.UtcNow;
                    }
                    
                    // 연결 상태 체크 및 정리
                    await PerformMaintenanceTasks(dataService);
                    
                    await Task.Delay(_updateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in data update service");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // 에러 시 5초 대기
                }
            }

            _logger.LogInformation("Data Update Service stopped");
        }

        private async Task BroadcastDeviceStatusSummary(IHubContext<DeviceDataHub> hubContext, IDataService dataService, List<string> activeDevices)
        {
            try
            {
                var deviceStatusList = new List<object>();
                
                foreach (var deviceId in activeDevices)
                {
                    var latestData = dataService.GetLatestDeviceData(deviceId);
                    if (latestData != null)
                    {
                        var deviceStatus = new
                        {
                            DeviceId = deviceId,
                            IsOnline = true,
                            LastUpdate = latestData.Timestamp,
                            ChannelCount = latestData.Channels.Count,
                            ActiveChannels = latestData.Channels.Count(c => c.Status != GPIMSWebServer.Models.ChannelStatus.Idle),
                            TotalPower = latestData.Channels.Sum(c => c.Power),
                            CANDataCount = latestData.CANData.Count,
                            LINDataCount = latestData.LINData.Count,
                            AuxDataCount = latestData.AuxData.Count,
                            AlarmCount = latestData.AlarmData.Count,
                            HasCriticalAlarms = latestData.AlarmData.Any(a => a.Severity == GPIMSWebServer.Models.AlarmSeverity.Critical)
                        };
                        deviceStatusList.Add(deviceStatus);
                    }
                }
                
                // 전체 클라이언트에게 디바이스 상태 요약 브로드캐스트
                await hubContext.Clients.All.SendAsync("ReceiveDeviceStatusSummary", deviceStatusList);
                
                _logger.LogDebug($"Broadcasted status summary for {deviceStatusList.Count} devices");
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
                // 여기서 주기적인 유지보수 작업을 수행할 수 있습니다
                var activeDevices = dataService.GetActiveDevices();
                
                // 활성 디바이스 수에 따른 로그 레벨 조정
                if (activeDevices.Count > 0)
                {
                    _logger.LogDebug($"Currently tracking {activeDevices.Count} active devices: {string.Join(", ", activeDevices)}");
                }
                else
                {
                    _logger.LogInformation("No active devices currently connected");
                }
                
                // 메모리 사용량 체크 (선택적)
                var memoryBefore = GC.GetTotalMemory(false);
                if (memoryBefore > 100 * 1024 * 1024) // 100MB 이상일 때
                {
                    _logger.LogInformation($"Memory usage: {memoryBefore / 1024 / 1024} MB, triggering GC");
                    GC.Collect();
                    var memoryAfter = GC.GetTotalMemory(true);
                    _logger.LogDebug($"Memory after GC: {memoryAfter / 1024 / 1024} MB");
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during maintenance tasks");
            }
        }
    }
}