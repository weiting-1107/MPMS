using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class TaskModel
    {
        public int TaskId { get; set; }

        [Required(ErrorMessage = "請選擇所屬專案階段")]
        [Display(Name = "專案階段")]
        public int PhaseId { get; set; }

        [Required(ErrorMessage = "請輸入任務名稱")]
        [Display(Name = "任務名稱")]
        [StringLength(200, ErrorMessage = "任務名稱長度不能超過 200 個字元")]
        public string TaskTitle { get; set; } = string.Empty;

        [Display(Name = "任務說明")]
        public string? TaskDesc { get; set; }

        [Required(ErrorMessage = "請指派主辦負責人")]
        [Display(Name = "主辦負責人")]
        public int OwnerUserId { get; set; }

        [Required(ErrorMessage = "請選擇截止日期")]
        [Display(Name = "截止日")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        [Display(Name = "任務狀態")]
        public string TaskStatus { get; set; } = "Todo"; // Todo, Progress, Reviewing, Done, Blocked

        [Display(Name = "優先度")]
        public string Priority { get; set; } = "Normal"; // Low, Normal, High

        [Display(Name = "卡關原因")]
        [StringLength(1000, ErrorMessage = "原因長度不能超過 1000 個字元")]
        public string? BlockedReason { get; set; }

        [Display(Name = "完工摘要")]
        public string? CompleteSummary { get; set; }

        [Display(Name = "送審時間")]
        public DateTime? ReviewRequestedAt { get; set; }

        [Display(Name = "完成時間")]
        public DateTime? DoneAt { get; set; }

        public int CreatedBy { get; set; }
        public int UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public byte[]? RowVersion { get; set; }
        public int ReviewRound { get; set; }
        public int? CurrentReviewId { get; set; }
        public int? ReviewerUserId { get; set; }
        public int? BackupReviewerUserId { get; set; }

        [Required(ErrorMessage = "請選擇計畫開始日期")]
        [Display(Name = "計畫開始日")]
        [DataType(DataType.Date)]
        public DateTime? PlannedStartDate { get; set; }

        [Display(Name = "是否為里程碑")]
        public bool IsMilestone { get; set; }

        public int GanttSortNo { get; set; }

        // Joined/Extra fields
        public string ProjectName { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string PhaseName { get; set; } = string.Empty;
        public string OwnerUserName { get; set; } = string.Empty;
        public string CreatorUserName { get; set; } = string.Empty;
        public string ReviewerUserName { get; set; } = string.Empty;
        public string BackupReviewerUserName { get; set; } = string.Empty;

        // Assistants
        public List<int> AssistUserIds { get; set; } = new List<int>();
        public List<string> AssistUserNames { get; set; } = new List<string>();

        // Predecessors
        public List<int> PredecessorTaskIds { get; set; } = new List<int>();
        public List<string> PredecessorTaskTitles { get; set; } = new List<string>();
        
        // Attachments
        public List<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
    }
}
