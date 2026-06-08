using System;

namespace MPMS.Models
{
    public class TaskAssist
    {
        public int TaskAssistId { get; set; }
        public int TaskId { get; set; }
        public int AssistUserId { get; set; }
        public bool IsReviewerCandidate { get; set; }
        public DateTime CreatedAt { get; set; }

        // Joined fields
        public string AssistUserName { get; set; } = string.Empty;
        public string AssistUserEmail { get; set; } = string.Empty;
    }
}
