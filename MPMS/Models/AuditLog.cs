using System;

namespace MPMS.Models
{
    public class AuditLog
    {
        public long AuditId { get; set; }
        public int ActorUserId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string TargetTable { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string? OldValueJson { get; set; }
        public string? NewValueJson { get; set; }
        public DateTime CreatedAt { get; set; }

        // Joined fields
        public string ActorAccount { get; set; } = string.Empty;
        public string ActorUserName { get; set; } = string.Empty;
    }
}
