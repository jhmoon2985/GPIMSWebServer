using System.ComponentModel.DataAnnotations;

namespace GPIMSWebServer.Models
{
    public class DeviceUpdate
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string DeviceId { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string CurrentVersion { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string TargetVersion { get; set; } = string.Empty;
        
        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;
        
        public long FileSize { get; set; }
        
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
        
        public UpdateStatus Status { get; set; } = UpdateStatus.Pending;
        
        public int Progress { get; set; } = 0;
        
        [StringLength(500)]
        public string ErrorMessage { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        [Required]
        public int CreatedByUserId { get; set; }
        
        [Required]
        [StringLength(50)]
        public string CreatedByUsername { get; set; } = string.Empty;
        
        // Navigation property
        public User? CreatedByUser { get; set; }
    }

    public enum UpdateStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Cancelled
    }

    public class DeviceUpdateViewModel
    {
        [Required]
        [Display(Name = "Device ID")]
        public string DeviceId { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Target Version")]
        public string TargetVersion { get; set; } = string.Empty;
        
        [Required]
        [Display(Name = "Update File")]
        public IFormFile? UpdateFile { get; set; }
        
        [Display(Name = "Description")]
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
    }

    public class UpdateHistoryViewModel
    {
        public int Id { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Description { get; set; } = string.Empty;
        public UpdateStatus Status { get; set; }
        public int Progress { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string CreatedByUsername { get; set; } = string.Empty;
        public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue 
            ? CompletedAt.Value - StartedAt.Value 
            : null;
    }
}