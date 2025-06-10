using GPIMSWebServer.Services;

namespace GPIMSWebServer.BackgroundServices
{
    public class DataUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DataUpdateService> _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(100);

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
                    
                    // Here you can add periodic tasks like:
                    // - Checking device connection status
                    // - Cleaning old data
                    // - Health checks
                    
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
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }

            _logger.LogInformation("Data Update Service stopped");
        }
    }
}