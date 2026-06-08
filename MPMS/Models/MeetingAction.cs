using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class MeetingAction
    {
        public int ActionId { get; set; }

        public int MeetingId { get; set; }

        public int? TaskId { get; set; }

        [Required(ErrorMessage = "請輸入臨時任務名稱")]
        [Display(Name = "任務名稱")]
        [StringLength(200, ErrorMessage = "名稱長度不能超過 200")]
        public string ActionTitle { get; set; } = string.Empty;

        [Required(ErrorMessage = "請指定負責人")]
        [Display(Name = "負責人")]
        public int OwnerUserId { get; set; }

        [Required(ErrorMessage = "請選擇截止日期")]
        [Display(Name = "截止日期")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; } = DateTime.Today.AddDays(7);

        [Display(Name = "說明/備註")]
        public string? ActionNote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Joined properties
        public string? OwnerUserName { get; set; }
        public string? AssociatedTaskTitle { get; set; }
    }
}
