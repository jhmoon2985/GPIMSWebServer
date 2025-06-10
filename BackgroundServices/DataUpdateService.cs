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

        public DataUpdateService(IServiceProvider serviceProvider, ILogger<DataUpdateService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Data Update Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DeviceDataHub>>();
                    
                    // 활성 디바이스 목록 가져오기
                    var activeDevices = dataService.GetActiveDevices();
                    
                    foreach (var deviceId in activeDevices)
                    {
                        var latestData = dataService.GetLatestDeviceData(deviceId);
                        if (latestData != null)
                        {
                            // 각 디바이스의 최신 데이터를 해당 그룹에 브로드캐스트
                            await hubContext.Clients.Group($"Device_{deviceId}")
                                .SendAsync("ReceiveDeviceData", latestData, stoppingToken);
                            
                            _logger.LogDebug($"Broadcasted data for device {deviceId}");
                        }
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

        private async Task PerformMaintenanceTasks(IDataService dataService)
        {
            try
            {
                // 여기서 주기적인 유지보수 작업을 수행할 수 있습니다
                // 예: 오래된 데이터 정리, 연결 상태 체크 등
                
                var activeDevices = dataService.GetActiveDevices();
                _logger.LogDebug($"Currently tracking {activeDevices.Count} active devices");
                
                // 필요시 추가 작업들...
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during maintenance tasks");
            }
        }
    }
}