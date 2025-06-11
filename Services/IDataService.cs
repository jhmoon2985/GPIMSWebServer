// Services/IDataService.cs
using GPIMSWebServer.Models;

namespace GPIMSWebServer.Services
{
    public interface IDataService
    {
        Task UpdateDeviceDataAsync(DeviceData deviceData);
        DeviceData? GetLatestDeviceData(string deviceId);
        List<DeviceData> GetDeviceDataHistory(string deviceId, int count = 100);
        List<string> GetActiveDevices();
        List<string> GetAllKnownDevices(); // 새로 추가
        bool IsDeviceOnline(string deviceId); // 새로 추가
        DateTime? GetLastHeartbeat(string deviceId); // 새로 추가
        Task<bool> ValidateDeviceDataAsync(DeviceData deviceData);
        Task BroadcastLatestDataAsync(string deviceId);
        Task CheckDeviceTimeoutsAsync(); // 새로 추가
        Task MarkDeviceOfflineAsync(string deviceId, string reason = "Manual"); // 새로 추가
        List<object> GetDeviceStatusSummary(); // 새로 추가
    }
}