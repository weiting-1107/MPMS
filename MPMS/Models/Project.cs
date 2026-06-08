using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class Project
    {
        public int ProjectId { get; set; }

        [Required(ErrorMessage = "請輸入專案代號")]
        [Display(Name = "專案代號")]
        [StringLength(50, ErrorMessage = "專案代號長度不能超過 50 個字元")]
        public string ProjectCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "請輸入專案名稱")]
        [Display(Name = "專案名稱")]
        [StringLength(200, ErrorMessage = "專案名稱長度不能超過 200 個字元")]
        public string ProjectName { get; set; } = string.Empty;

        [Display(Name = "專案說明")]
        public string? ProjectDesc { get; set; }

        [Required(ErrorMessage = "請指定專案 PM")]
        [Display(Name = "專案 PM")]
        public int PmUserId { get; set; }

        [Required(ErrorMessage = "請選擇開始日期")]
        [Display(Name = "開始日期")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "請選擇結束日期")]
        [Display(Name = "結束日期")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Range(0.00, 100.00, ErrorMessage = "進度必須介於 0% 到 100% 之間")]
        [Display(Name = "手動進度 (%)")]
        public decimal ManualProgressPct { get; set; }

        [Display(Name = "進度調整說明")]
        [StringLength(500, ErrorMessage = "說明不能超過 500 個字元")]
        public string? ProgressNote { get; set; }

        [Required(ErrorMessage = "請選擇專案狀態")]
        [Display(Name = "專案狀態")]
        public string ProjectStatus { get; set; } = "Planning"; // Planning, Active, Closed, Hold

        [Display(Name = "是否啟用")]
        public bool IsActive { get; set; } = true;

        // Joined fields
        public string PmUserName { get; set; } = string.Empty;
        public string PmEmail { get; set; } = string.Empty;
    }
}
