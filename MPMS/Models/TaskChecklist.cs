using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class TaskChecklist
    {
        public long ChecklistId { get; set; }

        [Required]
        public int TaskId { get; set; }

        [Required(ErrorMessage = "請輸入工作事項名稱")]
        [StringLength(300, ErrorMessage = "名稱長度不能超過 300 個字元")]
        [Display(Name = "名稱")]
        public string ChecklistTitle { get; set; } = string.Empty;

        [Display(Name = "說明")]
        public string? ChecklistDesc { get; set; }

        [Display(Name = "指定處理人")]
        public int? AssignedUserId { get; set; }

        [Display(Name = "預計完成日")]
        [DataType(DataType.Date)]
        public DateTime? DueDate { get; set; }

        public bool IsDone { get; set; }

        public int? DoneBy { get; set; }

        public DateTime? DoneAtUtc { get; set; }

        public int SortNo { get; set; }

        [StringLength(1000, ErrorMessage = "備註長度不能超過 1000 個字元")]
        public string? Note { get; set; }

        public int CreatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public byte[]? RowVersion { get; set; }

        // Joined properties
        public string? AssignedUserName { get; set; }
        public string? DoneByUserName { get; set; }
    }
}
