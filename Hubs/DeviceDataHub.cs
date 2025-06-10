// Hubs/DeviceDataHub.cs
using Microsoft.AspNetCore.SignalR;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;

namespace GPIMSWebServer.Hubs
{
    public class DeviceDataHub : Hub
    {
        private readonly IDataService _dataService;

        public DeviceDataHub(IDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task JoinDeviceGroup(string deviceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
        }

        public async Task LeaveDeviceGroup(string deviceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Device_{deviceId}");
        }

        public async Task RequestLatestData(string deviceId)
        {
            var latestData = _dataService.GetLatestDeviceData(deviceId);
            if (latestData != null)
            {
                await Clients.Caller.SendAsync("ReceiveDeviceData", latestData);
            }
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}