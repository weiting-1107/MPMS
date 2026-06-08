using System;
using System.ComponentModel.DataAnnotations;

namespace MPMS.Models
{
    public class ProjectPhase
    {
        public int PhaseId { get; set; }

        public int ProjectId { get; set; }

        [Required(ErrorMessage = "請輸入專案階段名稱")]
        [Display(Name = "專案階段名稱")]
        [StringLength(200, ErrorMessage = "名稱長度不能超過 200 個字元")]
        public string PhaseName { get; set; } = string.Empty;

        [Required(ErrorMessage = "請選擇開始日期")]
        [Display(Name = "開始日期")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "請選擇結束日期")]
        [Display(Name = "結束日期")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Display(Name = "排序值")]
        public int SortNo { get; set; }

        [Range(0.00, 100.00, ErrorMessage = "進度必須介於 0% 到 100% 之間")]
        [Display(Name = "手動進度 (%)")]
        public decimal ManualProgressPct { get; set; }

        [Display(Name = "階段狀態")]
        public string PhaseStatus { get; set; } = "Planned"; // Planned, Active, Completed, Voided

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Joined fields
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectCode { get; set; } = string.Empty;
    }
}
