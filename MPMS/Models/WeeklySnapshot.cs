using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class WeeklySnapshot
    {
        public int SnapshotId { get; set; }

        public int ProjectId { get; set; }

        [Required]
        public string WeekCode { get; set; } = string.Empty; // YYYY-WW

        public DateTime PeriodStartAtUtc { get; set; }

        public DateTime PeriodEndAtUtc { get; set; }

        public int SnapshotVersionNo { get; set; } = 1;

        public string SnapshotType { get; set; } = "Normal"; // Normal, Correction

        public bool IsCurrentVersion { get; set; } = true;

        public int? ParentSnapshotId { get; set; }

        public string? CorrectionReason { get; set; }

        public int SealedBy { get; set; }

        public DateTime SealedAtUtc { get; set; } = DateTime.UtcNow;

        public string? SnapshotNote { get; set; }

        public int? MeetingId { get; set; }

        // Joined properties
        public string? SealedByUserName { get; set; }
        public string? ProjectName { get; set; }
    }
}
