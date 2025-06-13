using GPIMSWebServer.Models;

namespace GPIMSWebServer.Services
{
    public interface IUserActivityService
    {
        Task LogActivityAsync(int userId, string username, ActivityType activityType, string description = "", string ipAddress = "", string userAgent = "");
        Task<List<UserActivityViewModel>> GetUserActivitiesAsync(int userId, int count = 50);
        Task<List<UserActivityViewModel>> GetAllActivitiesAsync(int count = 100);
        Task<List<UserActivityViewModel>> GetRecentLoginActivitiesAsync(int count = 20);
        Task CleanupOldActivitiesAsync(int daysToKeep = 30);
    }
}