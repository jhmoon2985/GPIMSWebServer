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
        Task<bool> ValidateDeviceDataAsync(DeviceData deviceData);
    }
}