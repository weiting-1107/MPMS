using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class Meeting
    {
        public int MeetingId { get; set; }

        [Display(Name = "週別")]
        public string? MeetingWeek { get; set; } // YYYY-WW

        [Required(ErrorMessage = "請選擇會議日期")]
        [Display(Name = "會議日期")]
        [DataType(DataType.Date)]
        public DateTime MeetingDate { get; set; } = DateTime.Today;

        [Display(Name = "關聯專案")]
        public int? ProjectId { get; set; }

        [Required(ErrorMessage = "請指定主持人")]
        [Display(Name = "主持人")]
        public int HostUserId { get; set; }

        [Display(Name = "會議記錄")]
        public string? MeetingNote { get; set; }

        [Display(Name = "建立時間")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Joined properties
        public string? ProjectName { get; set; }
        public string? HostUserName { get; set; }
    }
}
