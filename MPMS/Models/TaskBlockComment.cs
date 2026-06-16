using System;

namespace MPMS.Models
{
    public class TaskBlockComment
    {
        public int CommentId { get; set; }
        public int BlockId { get; set; }
        public string CommentType { get; set; } = "Comment"; // Comment, StatusChange, Assignment, Resolve, WeeklyFocus
        public string CommentText { get; set; } = string.Empty;
        public string? OldBlockStatus { get; set; }
        public string? NewBlockStatus { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }

        // Joined fields
        public string CreatorName { get; set; } = string.Empty;
    }
}
