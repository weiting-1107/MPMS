using System;
using System.Collections.Generic;

namespace MPMS.Models
{
    public class TaskBlockLog
    {
        public int BlockId { get; set; }
        public int TaskId { get; set; }
        public string BlockType { get; set; } = string.Empty; // Technical, Customer, Requirement, Permission, Data, WaitingOthers, Resource, Other
        public string BlockReason { get; set; } = string.Empty;
        public string HelpNeeded { get; set; } = string.Empty;
        public string ImpactLevel { get; set; } = string.Empty; // High, Medium, Low
        public int BlockedBy { get; set; }
        public DateTime BlockedAtUtc { get; set; }
        public int? AssignedHelperUserId { get; set; }
        public DateTime? ExpectedResolveDate { get; set; }
        public string BlockStatus { get; set; } = "Open"; // Open, InProgress, WaitingExternal, Resolved, Cancelled
        public string? ResolveSummary { get; set; }
        public int? ResolvedBy { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
        public bool IsWeeklyFocus { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public byte[]? RowVersion { get; set; }

        // Joined / View fields
        public string TaskTitle { get; set; } = string.Empty;
        public string BlockedByName { get; set; } = string.Empty;
        public string HelperName { get; set; } = string.Empty;
        public string ResolvedByName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string PhaseName { get; set; } = string.Empty;
        public string OwnerUserName { get; set; } = string.Empty;
        public int OwnerUserId { get; set; }
        public bool IsMilestone { get; set; }
        public DateTime DueDate { get; set; }
        public string? TaskStatus { get; set; }

        // Derived field for reporting/UI
        public int BlockedDays
        {
            get
            {
                var end = ResolvedAtUtc ?? DateTime.UtcNow;
                return (end - BlockedAtUtc).Days;
            }
        }

        // List of comments for details view
        public List<TaskBlockComment> Comments { get; set; } = new List<TaskBlockComment>();
    }
}
