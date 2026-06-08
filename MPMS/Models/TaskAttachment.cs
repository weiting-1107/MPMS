using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class TaskAttachment
    {
        public int AttachmentId { get; set; }
        public int TaskId { get; set; }
        public string BlobContainer { get; set; } = string.Empty;
        public string BlobPath { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string FileExt { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string? ContentType { get; set; }

        [Required(ErrorMessage = "請輸入完工說明或附件摘要")]
        [Display(Name = "附件或完工說明摘要")]
        public string SummaryText { get; set; } = string.Empty;
        
        public int UploadUserId { get; set; }
        public DateTime UploadedAt { get; set; }
        public bool IsVoid { get; set; }
        public string? VoidReason { get; set; }
        public string VisibilityType { get; set; } = "Public"; // Public, ProjectMember, Masked
        public string ScanStatus { get; set; } = "Passed"; // Pending, Passed, Failed

        // Joined fields
        public string UploadUserName { get; set; } = string.Empty;
        public string TaskTitle { get; set; } = string.Empty;
    }
}
