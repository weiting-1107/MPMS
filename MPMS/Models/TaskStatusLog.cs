using System;

namespace MPMS.Models
{
    public class TaskStatusLog
    {
        public int StatusLogId { get; set; }
        public int TaskId { get; set; }
        public string? OldStatus { get; set; }
        public string NewStatus { get; set; } = string.Empty;
        public string? ChangeReason { get; set; }
        public int ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }

        // Joined fields
        public string OperatorUserName { get; set; } = string.Empty;
    }
}
