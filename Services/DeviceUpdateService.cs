using Microsoft.EntityFrameworkCore;
using GPIMSWebServer.Data;
using GPIMSWebServer.Models;
using Microsoft.AspNetCore.SignalR;
using GPIMSWebServer.Hubs;

namespace GPIMSWebServer.Services
{
    public class DeviceUpdateService : IDeviceUpdateService
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataService _dataService;
        private readonly IHubContext<DeviceDataHub> _hubContext;
        private readonly ILogger<DeviceUpdateService> _logger;
        private readonly string _uploadPath;

        public DeviceUpdateService(
            ApplicationDbContext context, 
            IDataService dataService,
            IHubContext<DeviceDataHub> hubContext,
            ILogger<DeviceUpdateService> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _dataService = dataService;
            _hubContext = hubContext;
            _logger = logger;
            _uploadPath = Path.Combine(environment.ContentRootPath, "Uploads", "Firmware");
            
            // 업로드 디렉토리 생성
            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        public async Task<bool> CreateUpdateRequestAsync(DeviceUpdateViewModel model, int userId, string username)
        {
            try
            {
                if (model.UpdateFile == null || model.UpdateFile.Length == 0)
                {
                    _logger.LogWarning("Update file is empty or null");
                    return false;
                }

                // 파일 저장
                var fileName = $"{model.DeviceId}_{model.TargetVersion}_{DateTime.UtcNow:yyyyMMddHHmmss}_{model.UpdateFile.FileName}";
                var filePath = Path.Combine(_uploadPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.UpdateFile.CopyToAsync(stream);
                }

                // 현재 디바이스 버전 가져오기 (실제로는 디바이스에서 가져와야 함)
                var currentVersion = "1.0.0"; // 임시값

                var deviceUpdate = new DeviceUpdate
                {
                    DeviceId = model.DeviceId,
                    CurrentVersion = currentVersion,
                    TargetVersion = model.TargetVersion,
                    FileName = fileName,
                    FileSize = model.UpdateFile.Length,
                    Description = model.Description,
                    Status = UpdateStatus.Pending,
                    CreatedByUserId = userId,
                    CreatedByUsername = username,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DeviceUpdates.Add(deviceUpdate);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Update request created for device {model.DeviceId} by {username}");

                // SignalR로 업데이트 시작 알림
                await _hubContext.Clients.All.SendAsync("UpdateRequested", new
                {
                    DeviceId = model.DeviceId,
                    TargetVersion = model.TargetVersion,
                    CreatedBy = username
                });

                // 실제 업데이트 프로세스 시작 (백그라운드에서)
                _ = Task.Run(() => ProcessUpdateAsync(deviceUpdate.Id));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating update request for device {model.DeviceId}");
                return false;
            }
        }

        private async Task ProcessUpdateAsync(int updateId)
        {
            try
            {
                var update = await _context.DeviceUpdates.FindAsync(updateId);
                if (update == null) return;

                // 업데이트 시작
                update.Status = UpdateStatus.InProgress;
                update.StartedAt = DateTime.UtcNow;
                update.Progress = 0;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("UpdateProgress", new
                {
                    UpdateId = updateId,
                    DeviceId = update.DeviceId,
                    Progress = 0,
                    Status = "InProgress"
                });

                // 실제 업데이트 시뮬레이션 (실제로는 디바이스와 통신)
                for (int i = 1; i <= 100; i += 10)
                {
                    await Task.Delay(2000); // 2초씩 대기

                    update.Progress = i;
                    await _context.SaveChangesAsync();

                    await _hubContext.Clients.All.SendAsync("UpdateProgress", new
                    {
                        UpdateId = updateId,
                        DeviceId = update.DeviceId,
                        Progress = i,
                        Status = "InProgress"
                    });

                    _logger.LogDebug($"Update progress for device {update.DeviceId}: {i}%");
                }

                // 업데이트 완료
                update.Status = UpdateStatus.Completed;
                update.Progress = 100;
                update.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("UpdateCompleted", new
                {
                    UpdateId = updateId,
                    DeviceId = update.DeviceId,
                    Status = "Completed"
                });

                _logger.LogInformation($"Update completed for device {update.DeviceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing update {updateId}");
                
                var update = await _context.DeviceUpdates.FindAsync(updateId);
                if (update != null)
                {
                    update.Status = UpdateStatus.Failed;
                    update.ErrorMessage = ex.Message;
                    update.CompletedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await _hubContext.Clients.All.SendAsync("UpdateFailed", new
                    {
                        UpdateId = updateId,
                        DeviceId = update.DeviceId,
                        ErrorMessage = ex.Message
                    });
                }
            }
        }

        public async Task<List<UpdateHistoryViewModel>> GetUpdateHistoryAsync(string? deviceId = null, int count = 50)
        {
            var query = _context.DeviceUpdates.AsQueryable();

            if (!string.IsNullOrEmpty(deviceId))
            {
                query = query.Where(u => u.DeviceId == deviceId);
            }

            var updates = await query
                .OrderByDescending(u => u.CreatedAt)
                .Take(count)
                .Select(u => new UpdateHistoryViewModel
                {
                    Id = u.Id,
                    DeviceId = u.DeviceId,
                    CurrentVersion = u.CurrentVersion,
                    TargetVersion = u.TargetVersion,
                    FileName = u.FileName,
                    FileSize = u.FileSize,
                    Description = u.Description,
                    Status = u.Status,
                    Progress = u.Progress,
                    ErrorMessage = u.ErrorMessage,
                    CreatedAt = u.CreatedAt,
                    StartedAt = u.StartedAt,
                    CompletedAt = u.CompletedAt,
                    CreatedByUsername = u.CreatedByUsername
                })
                .ToListAsync();

            return updates;
        }

        public async Task<bool> UpdateProgressAsync(int updateId, int progress, UpdateStatus status, string? errorMessage = null)
        {
            try
            {
                var update = await _context.DeviceUpdates.FindAsync(updateId);
                if (update == null) return false;

                update.Progress = progress;
                update.Status = status;
                
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    update.ErrorMessage = errorMessage;
                }

                if (status == UpdateStatus.InProgress && !update.StartedAt.HasValue)
                {
                    update.StartedAt = DateTime.UtcNow;
                }

                if (status == UpdateStatus.Completed || status == UpdateStatus.Failed || status == UpdateStatus.Cancelled)
                {
                    update.CompletedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating progress for update {updateId}");
                return false;
            }
        }

        public async Task<DeviceUpdate?> GetUpdateByIdAsync(int id)
        {
            return await _context.DeviceUpdates
                .Include(u => u.CreatedByUser)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<bool> CancelUpdateAsync(int updateId, int userId)
        {
            try
            {
                var update = await _context.DeviceUpdates.FindAsync(updateId);
                if (update == null) return false;

                if (update.Status == UpdateStatus.Completed || update.Status == UpdateStatus.Failed)
                {
                    return false; // 이미 완료되었거나 실패한 업데이트는 취소할 수 없음
                }

                update.Status = UpdateStatus.Cancelled;
                update.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("UpdateCancelled", new
                {
                    UpdateId = updateId,
                    DeviceId = update.DeviceId
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling update {updateId}");
                return false;
            }
        }

        public async Task<List<string>> GetAvailableDevicesAsync()
        {
            // 온라인 디바이스 목록 가져오기
            var devices = _dataService.GetActiveDevices();
            return await Task.FromResult(devices);
        }
    }
}