using Microsoft.EntityFrameworkCore;
using GPIMSWebServer.Data;
using GPIMSWebServer.Models;

namespace GPIMSWebServer.Services
{
    public class UserActivityService : IUserActivityService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserActivityService> _logger;

        public UserActivityService(ApplicationDbContext context, ILogger<UserActivityService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogActivityAsync(int userId, string username, ActivityType activityType, string description = "", string ipAddress = "", string userAgent = "")
        {
            try
            {
                var activity = new UserActivity
                {
                    UserId = userId,
                    Username = username,
                    ActivityType = activityType.ToString(),
                    Description = description,
                    IpAddress = ipAddress,
                    UserAgent = userAgent.Length > 200 ? userAgent.Substring(0, 200) : userAgent,
                    CreatedAt = DateTime.UtcNow
                };

                _context.UserActivities.Add(activity);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Activity logged: {username} - {activityType}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging activity for user {username}");
            }
        }

        public async Task<List<UserActivityViewModel>> GetUserActivitiesAsync(int userId, int count = 50)
        {
            try
            {
                var activities = await _context.UserActivities
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(count)
                    .Select(a => new UserActivityViewModel
                    {
                        Id = a.Id,
                        Username = a.Username,
                        ActivityType = a.ActivityType,
                        Description = a.Description,
                        IpAddress = a.IpAddress,
                        CreatedAt = a.CreatedAt,
                        TimeAgo = GetTimeAgo(a.CreatedAt)
                    })
                    .ToListAsync();

                return activities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving activities for user {userId}");
                return new List<UserActivityViewModel>();
            }
        }

        public async Task<List<UserActivityViewModel>> GetAllActivitiesAsync(int count = 100)
        {
            try
            {
                var activities = await _context.UserActivities
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(count)
                    .Select(a => new UserActivityViewModel
                    {
                        Id = a.Id,
                        Username = a.Username,
                        ActivityType = a.ActivityType,
                        Description = a.Description,
                        IpAddress = a.IpAddress,
                        CreatedAt = a.CreatedAt,
                        TimeAgo = GetTimeAgo(a.CreatedAt)
                    })
                    .ToListAsync();

                return activities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all activities");
                return new List<UserActivityViewModel>();
            }
        }

        public async Task<List<UserActivityViewModel>> GetRecentLoginActivitiesAsync(int count = 20)
        {
            try
            {
                var activities = await _context.UserActivities
                    .Where(a => a.ActivityType == "Login" || a.ActivityType == "Logout")
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(count)
                    .Select(a => new UserActivityViewModel
                    {
                        Id = a.Id,
                        Username = a.Username,
                        ActivityType = a.ActivityType,
                        Description = a.Description,
                        IpAddress = a.IpAddress,
                        CreatedAt = a.CreatedAt,
                        TimeAgo = GetTimeAgo(a.CreatedAt)
                    })
                    .ToListAsync();

                return activities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving login activities");
                return new List<UserActivityViewModel>();
            }
        }

        public async Task CleanupOldActivitiesAsync(int daysToKeep = 30)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var oldActivities = await _context.UserActivities
                    .Where(a => a.CreatedAt < cutoffDate)
                    .ToListAsync();

                if (oldActivities.Any())
                {
                    _context.UserActivities.RemoveRange(oldActivities);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation($"Cleaned up {oldActivities.Count} old activities older than {daysToKeep} days");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old activities");
            }
        }

        private static string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes}m ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours}h ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)timeSpan.TotalDays}d ago";
            
            return dateTime.ToString("MMM dd, yyyy");
        }
    }
}