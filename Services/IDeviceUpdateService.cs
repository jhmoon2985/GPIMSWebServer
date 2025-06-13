using GPIMSWebServer.Models;

namespace GPIMSWebServer.Services
{
    public interface IDeviceUpdateService
    {
        Task<bool> CreateUpdateRequestAsync(DeviceUpdateViewModel model, int userId, string username);
        Task<List<UpdateHistoryViewModel>> GetUpdateHistoryAsync(string? deviceId = null, int count = 50);
        Task<bool> UpdateProgressAsync(int updateId, int progress, UpdateStatus status, string? errorMessage = null);
        Task<DeviceUpdate?> GetUpdateByIdAsync(int id);
        Task<bool> CancelUpdateAsync(int updateId, int userId);
        Task<List<string>> GetAvailableDevicesAsync();
    }
}