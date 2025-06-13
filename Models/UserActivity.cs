using System.ComponentModel.DataAnnotations;

namespace GPIMSWebServer.Models
{
    public class UserActivity
    {
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string ActivityType { get; set; } = string.Empty; // Login, Logout, ViewDevice, etc.
        
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;
        
        [StringLength(45)]
        public string IpAddress { get; set; } = string.Empty;
        
        [StringLength(200)]
        public string UserAgent { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public User? User { get; set; }
    }

    public enum ActivityType
    {
        Login,
        Logout,
        ViewDevice,
        ViewChannels,
        ViewMonitoring,
        CreateUser,
        EditUser,
        DeleteUser,
        EditProfile
    }

    public class UserActivityViewModel
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string ActivityType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string TimeAgo { get; set; } = string.Empty;
    }
}