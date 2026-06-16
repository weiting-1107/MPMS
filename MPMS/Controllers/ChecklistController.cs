using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPMS.Models;
using MPMS.Repositories;

namespace MPMS.Controllers
{
    [Authorize]
    public class ChecklistController : Controller
    {
        private readonly ChecklistRepository _checklistRepo;
        private readonly TaskRepository _taskRepo;
        private readonly UserRepository _userRepo;

        public ChecklistController(ChecklistRepository checklistRepo, TaskRepository taskRepo, UserRepository userRepo)
        {
            _checklistRepo = checklistRepo;
            _taskRepo = taskRepo;
            _userRepo = userRepo;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        [HttpGet]
        public async Task<IActionResult> GetChecklists(int taskId)
        {
            var lists = await _checklistRepo.GetChecklistsByTaskIdAsync(taskId);
            return Json(new { success = true, data = lists });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskChecklist model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = string.Join("\n", errors) });
            }

            var task = await _taskRepo.GetTaskByIdAsync(model.TaskId);
            if (task == null) return Json(new { success = false, message = "找不到該任務" });
            if (task.TaskStatus == "Reviewing" || task.TaskStatus == "Done")
            {
                return Json(new { success = false, message = "該任務已在審查中或已結案，無法新增工作事項" });
            }

            model.CreatedBy = GetCurrentUserId();

            try
            {
                var id = await _checklistRepo.CreateChecklistAsync(model);
                var createdItem = await _checklistRepo.GetChecklistByIdAsync(id);
                return Json(new { success = true, data = createdItem });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "新增失敗：" + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Update([FromBody] TaskChecklist model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = string.Join("\n", errors) });
            }

            var task = await _taskRepo.GetTaskByIdAsync(model.TaskId);
            if (task == null) return Json(new { success = false, message = "找不到該任務" });
            if (task.TaskStatus == "Reviewing" || task.TaskStatus == "Done")
            {
                return Json(new { success = false, message = "該任務已在審查中或已結案，無法修改工作事項" });
            }

            try
            {
                var rows = await _checklistRepo.UpdateChecklistAsync(model);
                if (rows > 0)
                {
                    var updatedItem = await _checklistRepo.GetChecklistByIdAsync(model.ChecklistId);
                    return Json(new { success = true, data = updatedItem });
                }
                return Json(new { success = false, message = "更新失敗或資料已被修改" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "更新失敗：" + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStatus(long checklistId, bool isDone, string rowVersionStr)
        {
            try
            {
                var checklist = await _checklistRepo.GetChecklistByIdAsync(checklistId);
                if (checklist == null) return Json(new { success = false, message = "找不到該工作事項" });
                var task = await _taskRepo.GetTaskByIdAsync(checklist.TaskId);
                if (task == null) return Json(new { success = false, message = "找不到該任務" });
                if (task.TaskStatus == "Reviewing" || task.TaskStatus == "Done")
                {
                    return Json(new { success = false, message = "該任務已在審查中或已結案，無法變更狀態" });
                }

                byte[] rowVersion = Convert.FromBase64String(rowVersionStr);
                int userId = GetCurrentUserId();
                var rows = await _checklistRepo.ToggleStatusAsync(checklistId, isDone, userId, rowVersion);
                if (rows > 0)
                {
                    var updatedItem = await _checklistRepo.GetChecklistByIdAsync(checklistId);
                    return Json(new { success = true, data = updatedItem });
                }
                return Json(new { success = false, message = "狀態更新失敗或資料已被修改" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "狀態更新失敗：" + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(long checklistId, string rowVersionStr)
        {
            try
            {
                var checklist = await _checklistRepo.GetChecklistByIdAsync(checklistId);
                if (checklist == null) return Json(new { success = false, message = "找不到該工作事項" });
                var task = await _taskRepo.GetTaskByIdAsync(checklist.TaskId);
                if (task == null) return Json(new { success = false, message = "找不到該任務" });
                if (task.TaskStatus == "Reviewing" || task.TaskStatus == "Done")
                {
                    return Json(new { success = false, message = "該任務已在審查中或已結案，無法刪除工作事項" });
                }

                byte[] rowVersion = Convert.FromBase64String(rowVersionStr);
                var rows = await _checklistRepo.DeleteChecklistAsync(checklistId, rowVersion);
                if (rows > 0)
                {
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = "刪除失敗或資料已被修改" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "刪除失敗：" + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrder(int taskId, [FromBody] List<long> sortedIds)
        {
            try
            {
                var task = await _taskRepo.GetTaskByIdAsync(taskId);
                if (task == null) return Json(new { success = false, message = "找不到該任務" });
                if (task.TaskStatus == "Reviewing" || task.TaskStatus == "Done")
                {
                    return Json(new { success = false, message = "該任務已在審查中或已結案，無法更新排序" });
                }

                bool result = await _checklistRepo.UpdateOrderAsync(taskId, sortedIds);
                return Json(new { success = result, message = result ? "" : "更新排序失敗" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "更新排序發生錯誤：" + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportReport(int taskId)
        {
            try
            {
                var task = await _taskRepo.GetTaskByIdAsync(taskId);
                if (task == null) return NotFound("Task not found.");

                var checklists = await _checklistRepo.GetChecklistsByTaskIdAsync(taskId);

                var csv = new StringBuilder();
                // Write BOM for Excel UTF-8 support
                csv.Append("\uFEFF");
                
                // Report Header
                csv.AppendLine($"報表編號,RPT006");
                csv.AppendLine($"報表名稱,任務工作事項檢核清單");
                csv.AppendLine($"匯出時間,{DateTime.Now:yyyy/MM/dd HH:mm:ss}");
                csv.AppendLine($"任務名稱,{task.TaskTitle}");
                csv.AppendLine($"負責人,{task.OwnerUserName}");
                csv.AppendLine($"專案名稱,{task.ProjectName}");
                csv.AppendLine();

                // Column Headers
                csv.AppendLine("狀態,工作事項名稱,預計完成日,指定處理人,實際完成人,實際完成時間,備註");

                foreach (var item in checklists)
                {
                    var statusStr = item.IsDone ? "已完成" : "未完成";
                    var titleStr = EscapeCsv(item.ChecklistTitle);
                    var dueDateStr = item.DueDate?.ToString("yyyy/MM/dd") ?? "";
                    var assignedStr = EscapeCsv(item.AssignedUserName ?? "");
                    var doneByStr = EscapeCsv(item.DoneByUserName ?? "");
                    var doneAtStr = item.DoneAtUtc?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "";
                    var noteStr = EscapeCsv(item.Note ?? "");

                    csv.AppendLine($"{statusStr},{titleStr},{dueDateStr},{assignedStr},{doneByStr},{doneAtStr},{noteStr}");
                }

                var fileName = $"RPT006_Checklist_{task.TaskId}_{DateTime.Now:yyyyMMdd}.csv";
                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error generating report: " + ex.Message);
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }
    }
}
