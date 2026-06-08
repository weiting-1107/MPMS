using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class TaskReview
    {
        public int ReviewId { get; set; }
        public int TaskId { get; set; }
        public int ReviewerUserId { get; set; }

        [Required(ErrorMessage = "請選擇審查結果")]
        [Display(Name = "審查結果")]
        public string ReviewResult { get; set; } = string.Empty; // Approved, Rejected

        [Required(ErrorMessage = "請輸入審查意見或退回原因")]
        [Display(Name = "審查意見")]
        public string ReviewComment { get; set; } = string.Empty;

        public DateTime ReviewedAt { get; set; }
        public string SourceType { get; set; } = "PeerReview"; // PeerReview, MeetingReturn, AdminOverride
        public byte[]? RowVersion { get; set; }
        public int ReviewRound { get; set; }

        // Joined fields
        public string ReviewerUserName { get; set; } = string.Empty;
        public string TaskTitle { get; set; } = string.Empty;
    }
}
