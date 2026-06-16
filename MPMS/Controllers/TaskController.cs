using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using MPMS.Models;
using MPMS.Repositories;
using MPMS.Services;
using MPMS.Services.Storage;

namespace MPMS.Controllers
{
    [Authorize]
    public class TaskController : Controller
    {
        private readonly TaskRepository _taskRepository;
        private readonly ProjectRepository _projectRepository;
        private readonly ProjectPhaseRepository _projectPhaseRepository;
        private readonly IAttachmentStorageService _storageService;
        private readonly AuditService _auditService;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<MPMS.Hubs.MeetingHub> _hubContext;
        private readonly NotificationService _notificationService;
        private readonly BlockRepository _blockRepository;

        public TaskController(
            TaskRepository taskRepository,
            ProjectRepository projectRepository,
            ProjectPhaseRepository projectPhaseRepository,
            IAttachmentStorageService storageService,
            AuditService auditService,
            Microsoft.AspNetCore.SignalR.IHubContext<MPMS.Hubs.MeetingHub> hubContext,
            NotificationService notificationService,
            BlockRepository blockRepository)
        {
            _taskRepository = taskRepository;
            _projectRepository = projectRepository;
            _projectPhaseRepository = projectPhaseRepository;
            _storageService = storageService;
            _auditService = auditService;
            _hubContext = hubContext;
            _notificationService = notificationService;
            _blockRepository = blockRepository;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // TSK-001: 建立任務 - 畫面 (僅限 PM / Admin)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Create(int phaseId)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(phaseId);
            if (phase == null) return NotFound();

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            if (project == null) return NotFound();

            var activeUsers = await _taskRepository.GetActiveUsersAsync();
            var projectTasks = await _taskRepository.GetTasksByProjectIdAsync(project.ProjectId);

            ViewBag.Users = activeUsers;
            ViewBag.Phase = phase;
            ViewBag.Project = project;
            ViewBag.ProjectTasks = projectTasks.ToList();

            return View(new TaskModel 
            { 
                PhaseId = phaseId,
                ProjectId = project.ProjectId,
                PlannedStartDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7),
                Priority = "Normal"
            });
        }

        // TSK-001: 建立任務 - 提交 (僅限 PM / Admin)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TaskModel model, int[] selectedAssistants, int[] selectedPredecessors)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(model.PhaseId);
            if (phase == null) return NotFound();

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            if (project == null) return NotFound();

            model.ProjectId = project.ProjectId;

            // TSK-002: 負責人與協辦人防呆
            if (selectedAssistants != null && selectedAssistants.Contains(model.OwnerUserId))
            {
                ModelState.AddModelError(string.Empty, "主辦負責人不可同時出現在協辦人員名單中！");
            }

            // Date validation
            if (model.PlannedStartDate.HasValue && model.PlannedStartDate.Value > model.DueDate)
            {
                ModelState.AddModelError("PlannedStartDate", "計畫開始日不得晚於截止日！");
            }
            if (model.PlannedStartDate.HasValue && model.PlannedStartDate.Value < project.StartDate)
            {
                ModelState.AddModelError("PlannedStartDate", $"計畫開始日不得早於專案開始日 ({project.StartDate:yyyy-MM-dd})！");
            }
            if (model.DueDate > project.EndDate)
            {
                ModelState.AddModelError("DueDate", $"截止日不得晚於專案結束日 ({project.EndDate:yyyy-MM-dd})！");
            }

            // Phase warning check
            bool isCrossPhase = false;
            if (model.PlannedStartDate.HasValue && (model.PlannedStartDate.Value < phase.StartDate || model.DueDate > phase.EndDate))
            {
                isCrossPhase = true;
                string? crossPhaseReason = Request.Form["CrossPhaseReason"];
                if (string.IsNullOrWhiteSpace(crossPhaseReason))
                {
                    ModelState.AddModelError(string.Empty, $"計畫時間已超出所屬階段的區間 ({phase.StartDate:yyyy-MM-dd} ~ {phase.EndDate:yyyy-MM-dd})，請填寫跨階段原因！");
                    ViewBag.ShowCrossPhaseWarning = true;
                }
            }

            if (ModelState.IsValid)
            {
                model.CreatedBy = CurrentUserId;
                model.UpdatedBy = CurrentUserId;
                if (selectedAssistants != null)
                {
                    model.AssistUserIds.AddRange(selectedAssistants);
                }
                if (selectedPredecessors != null)
                {
                    model.PredecessorTaskIds.AddRange(selectedPredecessors);
                }

                try
                {
                    var success = await _taskRepository.CreateTaskAsync(model);
                    if (success)
                    {
                        await _auditService.LogAsync(
                            CurrentUserId, 
                            "CreateTask", 
                            "MPMS_TASK", 
                            model.TaskId.ToString(), 
                            null, 
                            new { Task = model, CrossPhaseReason = Request.Form["CrossPhaseReason"].ToString() }
                        );

                        // Notify Owner
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: model.OwnerUserId,
                            eventType: "NewTask",
                            title: "新任務指派 📋",
                            message: $"您被指派了新任務「{model.TaskTitle}」，截止日期：{model.DueDate:yyyy-MM-dd}。",
                            refType: "TASK",
                            refId: model.TaskId
                        );

                        // Notify Assistants
                        if (selectedAssistants != null)
                        {
                            foreach (var assistantId in selectedAssistants)
                            {
                                await _notificationService.SendNotificationAsync(
                                    receiverUserId: assistantId,
                                    eventType: "NewTask",
                                    title: "新任務指派 📋",
                                    message: $"您被指派為任務「{model.TaskTitle}」的協辦人員，截止日期：{model.DueDate:yyyy-MM-dd}。",
                                    refType: "TASK",
                                    refId: model.TaskId
                                );
                            }
                        }

                        return RedirectToAction("Phases", "Project", new { projectId = phase.ProjectId });
                    }
                    ModelState.AddModelError(string.Empty, "建立任務失敗。");
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, $"建立任務發生錯誤：{ex.Message}");
                }
            }

            var activeUsers = await _taskRepository.GetActiveUsersAsync();
            ViewBag.Users = activeUsers;
            ViewBag.Phase = phase;
            ViewBag.Project = project;
            ViewBag.ProjectTasks = (await _taskRepository.GetTasksByProjectIdAsync(project.ProjectId)).ToList();
            return View(model);
        }

        // TSK-001: 編輯任務 - 畫面 (僅限 PM / Admin)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Edit(int id)
        {
            var task = await _taskRepository.GetTaskByIdAsync(id);
            if (task == null) return NotFound();

            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(task.PhaseId);
            ViewBag.Phase = phase;
            var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
            ViewBag.Project = project;

            var activeUsers = await _taskRepository.GetActiveUsersAsync();
            ViewBag.Users = activeUsers;

            var projectTasks = await _taskRepository.GetTasksByProjectIdAsync(task.ProjectId);
            ViewBag.ProjectTasks = projectTasks.Where(t => t.TaskId != id).ToList();

            return View(task);
        }

        // TSK-001: 編輯任務 - 提交 (僅限 PM / Admin)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TaskModel model, int[] selectedAssistants, int[] selectedPredecessors)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(model.PhaseId);
            if (phase == null) return NotFound();

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            if (project == null) return NotFound();

            model.ProjectId = project.ProjectId;

            if (selectedAssistants != null && selectedAssistants.Contains(model.OwnerUserId))
            {
                ModelState.AddModelError(string.Empty, "主辦負責人不可同時出現在協辦人員名單中！");
            }

            // Date validation
            if (model.PlannedStartDate.HasValue && model.PlannedStartDate.Value > model.DueDate)
            {
                ModelState.AddModelError("PlannedStartDate", "計畫開始日不得晚於截止日！");
            }
            if (model.PlannedStartDate.HasValue && model.PlannedStartDate.Value < project.StartDate)
            {
                ModelState.AddModelError("PlannedStartDate", $"計畫開始日不得早於專案開始日 ({project.StartDate:yyyy-MM-dd})！");
            }
            if (model.DueDate > project.EndDate)
            {
                ModelState.AddModelError("DueDate", $"截止日不得晚於專案結束日 ({project.EndDate:yyyy-MM-dd})！");
            }

            // Phase warning check
            bool isCrossPhase = false;
            if (model.PlannedStartDate.HasValue && (model.PlannedStartDate.Value < phase.StartDate || model.DueDate > phase.EndDate))
            {
                isCrossPhase = true;
                string? crossPhaseReason = Request.Form["CrossPhaseReason"];
                if (string.IsNullOrWhiteSpace(crossPhaseReason))
                {
                    ModelState.AddModelError(string.Empty, $"計畫時間已超出所屬階段的區間 ({phase.StartDate:yyyy-MM-dd} ~ {phase.EndDate:yyyy-MM-dd})，請填寫跨階段原因！");
                    ViewBag.ShowCrossPhaseWarning = true;
                }
            }

            if (ModelState.IsValid)
            {
                var oldTask = await _taskRepository.GetTaskByIdAsync(model.TaskId);
                if (oldTask == null) return NotFound();

                model.UpdatedBy = CurrentUserId;
                if (selectedAssistants != null)
                {
                    model.AssistUserIds.AddRange(selectedAssistants);
                }
                if (selectedPredecessors != null)
                {
                    model.PredecessorTaskIds.AddRange(selectedPredecessors);
                }

                try
                {
                    var success = await _taskRepository.UpdateTaskAsync(model);
                    if (success)
                    {
                        var updatedTask = await _taskRepository.GetTaskByIdAsync(model.TaskId);
                        await _auditService.LogAsync(
                            CurrentUserId, 
                            "UpdateTask", 
                            "MPMS_TASK", 
                            model.TaskId.ToString(), 
                            oldTask, 
                            new { Task = updatedTask, CrossPhaseReason = Request.Form["CrossPhaseReason"].ToString() }
                        );

                        // Notify new/old owner if owner changed
                        if (oldTask.OwnerUserId != model.OwnerUserId)
                        {
                            // Notify New Owner
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: model.OwnerUserId,
                                eventType: "OwnerChanged",
                                title: "任務負責人變更 🔄",
                                message: $"您被指派為任務「{model.TaskTitle}」的新負責人，截止日期：{model.DueDate:yyyy-MM-dd}。",
                                refType: "TASK",
                                refId: model.TaskId
                            );

                            // Notify Old Owner
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: oldTask.OwnerUserId,
                                eventType: "OwnerChanged",
                                title: "任務負責人變更 🔄",
                                message: $"您不再擔任任務「{model.TaskTitle}」的負責人（已由 PM 變更）。",
                                refType: "TASK",
                                refId: model.TaskId
                            );
                        }

                        return RedirectToAction("Phases", "Project", new { projectId = oldTask.ProjectId });
                    }
                    ModelState.AddModelError(string.Empty, "更新任務失敗。");
                }
                catch (System.Data.DBConcurrencyException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    ModelState.AddModelError(string.Empty, ex.Message);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, $"更新任務時發生未預期錯誤：{ex.Message}");
                }
            }

            ViewBag.Phase = phase;
            ViewBag.Project = project;

            var activeUsers = await _taskRepository.GetActiveUsersAsync();
            ViewBag.Users = activeUsers;

            var projectTasks = await _taskRepository.GetTasksByProjectIdAsync(model.ProjectId);
            ViewBag.ProjectTasks = projectTasks.Where(t => t.TaskId != model.TaskId).ToList();

            return View(model);
        }

        // 任務詳情 (所有角色可查看)
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var task = await _taskRepository.GetTaskByIdAsync(id);
            if (task == null) return NotFound();

            var statusLogs = await _taskRepository.GetTaskStatusLogsAsync(id);
            var reviews = await _taskRepository.GetTaskReviewsAsync(id);

            ViewBag.StatusLogs = statusLogs;
            ViewBag.Reviews = reviews;
            ViewBag.ActiveBlockLog = await _blockRepository.GetActiveBlockLogByTaskIdAsync(id);
            ViewBag.AllBlockLogs = await _blockRepository.GetBlockLogsByTaskIdAsync(id);
            ViewBag.Users = await _taskRepository.GetActiveUsersAsync();

            return View(task);
        }

        // TSK-003: 任務狀態回報 / TSK-004: 卡關回報
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int taskId, string newStatus, string? reason, string rowVersionStr)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return NotFound();

            // V0.7 卡關限制：禁止直接透過 UpdateStatus 切換到 Blocked 狀態
            if (newStatus == "Blocked")
            {
                TempData["ErrorMessage"] = "回報卡關請使用專屬的「卡關申報」功能！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            // V0.7 卡關限制：卡關中的任務不可直接修改狀態，必須先解除卡關
            if (task.TaskStatus == "Blocked")
            {
                TempData["ErrorMessage"] = "卡關中的任務狀態無法直接修改，請先進行「解除卡關」！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            // Employee 權限控管：只能更新自己負責任務
            var isUserAdmin = User.IsInRole("ADMIN");
            var isUserPm = User.IsInRole("PM");
            if (!isUserAdmin && !isUserPm && task.OwnerUserId != CurrentUserId)
            {
                return Forbid();
            }

            // TSK-004: 狀態改為 Blocked 時必須填寫卡關原因。
            if (newStatus == "Blocked" && string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "回報卡關狀態時，必須填寫卡關原因！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            try
            {
                byte[] rowVersion = Convert.FromBase64String(rowVersionStr);
                var success = await _taskRepository.UpdateTaskStatusAsync(taskId, newStatus, reason, CurrentUserId, rowVersion);
                if (success)
                {
                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                    if (newStatus == "Blocked")
                    {
                        var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                        if (project != null)
                        {
                            // Notify PM
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: project.PmUserId,
                                eventType: "TaskBlocked",
                                title: "任務卡關回報 🚨",
                                message: $"成員 {task.OwnerUserName} 已將任務「{task.TaskTitle}」標記為卡關。原因：{reason}",
                                refType: "TASK",
                                refId: taskId
                            );
                        }

                        // Notify Assistants
                        if (task.AssistUserIds != null)
                        {
                            foreach (var assistantId in task.AssistUserIds)
                            {
                                await _notificationService.SendNotificationAsync(
                                    receiverUserId: assistantId,
                                    eventType: "TaskBlocked",
                                    title: "任務卡關回報 🚨",
                                    message: $"成員 {task.OwnerUserName} 已將任務「{task.TaskTitle}」標記為卡關。原因：{reason}",
                                    refType: "TASK",
                                    refId: taskId
                                );
                            }
                        }
                    }

                    TempData["SuccessMessage"] = $"任務狀態已成功更新為 {newStatus}。";
                }
                else
                {
                    TempData["ErrorMessage"] = "任務狀態更新失敗。";
                }
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"更新狀態發生錯誤：{ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id = taskId });
        }

        // ATT-001: 附件上傳 (限制類型與大小)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadAttachment(int taskId, IFormFile file, string summaryText, string visibilityType)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return NotFound();

            // Employee 權限控管：一般人員只能上傳自己負責任務的附件
            if (!User.IsInRole("ADMIN") && !User.IsInRole("PM") && task.OwnerUserId != CurrentUserId)
            {
                return Forbid();
            }

            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "請選擇要上傳的檔案。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            if (string.IsNullOrWhiteSpace(summaryText))
            {
                TempData["ErrorMessage"] = "上傳附件時必須填寫完工說明或附件摘要！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            // 1. 檔案大小限制：50MB
            const long maxFileSize = 50 * 1024 * 1024;
            if (file.Length > maxFileSize)
            {
                TempData["ErrorMessage"] = "檔案大小不能超過 50MB！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            // 2. 檔案格式防呆 (PDF, Word, Excel, PowerPoint, Image, ZIP)
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar", ".png", ".jpg", ".jpeg", ".gif" };
            if (!allowedExtensions.Contains(extension))
            {
                TempData["ErrorMessage"] = "上傳失敗！不支援的檔案類型。僅允許 PDF、Word、Excel、PPT、ZIP 及圖片檔案。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            try
            {
                // Save physical file
                using var stream = file.OpenReadStream();
                var savedPath = await _storageService.SaveFileAsync("task-attachments", file.FileName, stream);

                var attachment = new TaskAttachment
                {
                    TaskId = taskId,
                    BlobContainer = "task-attachments",
                    BlobPath = savedPath,
                    OriginalFileName = file.FileName,
                    StoredFileName = Path.GetFileName(savedPath),
                    FileExt = extension,
                    FileSizeBytes = file.Length,
                    ContentType = file.ContentType,
                    SummaryText = summaryText,
                    UploadUserId = CurrentUserId,
                    VisibilityType = string.IsNullOrEmpty(visibilityType) ? "Public" : visibilityType
                };

                var success = await _taskRepository.AddAttachmentAsync(attachment);
                if (success)
                {
                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "UploadAttachment", 
                        "MPMS_TASK_ATTACHMENT", 
                        attachment.OriginalFileName, 
                        null, 
                        attachment
                    );
                    TempData["SuccessMessage"] = "附件上傳成功！系統背景正在進行安全性檢查...";
                }
                else
                {
                    TempData["ErrorMessage"] = "附件資料儲存失敗。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"附件上傳發生錯誤：{ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id = taskId });
        }

        // ATT-002: 附件安全下載
        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            var attachment = await _taskRepository.GetAttachmentByIdAsync(id);
            if (attachment == null || attachment.IsVoid) return NotFound("此附件不存在或已被作廢。");

            var task = await _taskRepository.GetTaskByIdAsync(attachment.TaskId);
            if (task == null) return NotFound("關聯任務不存在。");

            // 檢查病毒掃描狀態：非上傳者與 Admin/PM，不得下載 scan_status != 'Passed' 的附件
            var isUserAdmin = User.IsInRole("ADMIN");
            var isUserPm = User.IsInRole("PM");
            var isOwner = task.OwnerUserId == CurrentUserId;

            if (attachment.ScanStatus != "Passed" && attachment.UploadUserId != CurrentUserId && !isUserAdmin && !isUserPm)
            {
                TempData["ErrorMessage"] = "該檔案尚未通過安全性掃描，目前無法下載。";
                return RedirectToAction(nameof(Details), new { id = attachment.TaskId });
            }

            // 敏感附件 (ProjectMember 或 Masked) 的下載權限控制
            if (attachment.VisibilityType == "ProjectMember" || attachment.VisibilityType == "Masked")
            {
                var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                var isProjectPm = isUserPm && project != null && project.PmUserId == CurrentUserId;
                var isAssistant = task.AssistUserIds.Contains(CurrentUserId);
                var isReviewer = task.ReviewerUserId == CurrentUserId || task.BackupReviewerUserId == CurrentUserId;

                if (!isUserAdmin && !isProjectPm && !isOwner && !isAssistant && !isReviewer)
                {
                    return Forbid(); // 無權限下載此機敏附件
                }
            }

            var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var physicalPath = Path.Combine(env.WebRootPath, attachment.BlobPath.TrimStart('/'));
            if (!System.IO.File.Exists(physicalPath))
            {
                return NotFound("實體檔案不存在。");
            }

            var contentType = attachment.ContentType ?? "application/octet-stream";
            return File(System.IO.File.OpenRead(physicalPath), contentType, attachment.OriginalFileName);
        }

        // ATT-003: 附件作廢 (僅限 Admin)
        [HttpPost]
        [Authorize(Roles = "ADMIN")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidAttachment(int attachmentId, int taskId, string voidReason)
        {
            if (string.IsNullOrWhiteSpace(voidReason))
            {
                TempData["ErrorMessage"] = "作廢附件必須填寫原因！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            var success = await _taskRepository.VoidAttachmentAsync(attachmentId, voidReason, CurrentUserId);
            if (success)
            {
                await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);
                await _auditService.LogAsync(
                    CurrentUserId, 
                    "VoidAttachment", 
                    "MPMS_TASK_ATTACHMENT", 
                    attachmentId.ToString(), 
                    null, 
                    new { attachment_id = attachmentId, void_reason = voidReason }
                );
                TempData["SuccessMessage"] = "附件已成功標記為作廢。";
            }
            else
            {
                TempData["ErrorMessage"] = "附件作廢處理失敗。";
            }

            return RedirectToAction(nameof(Details), new { id = taskId });
        }

        // REV-001: 任務送審 (至少 1 附件，且摘要需 >= 30 字)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitToReview(int taskId, string completeSummary, string rowVersionStr)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return NotFound();

            // V0.7 卡關限制：Blocked 任務不可送審
            if (task.TaskStatus == "Blocked")
            {
                TempData["ErrorMessage"] = "完工送審失敗！卡關中的任務不可送審，必須先解除卡關。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            // Employee 權限控管
            if (!User.IsInRole("ADMIN") && !User.IsInRole("PM") && task.OwnerUserId != CurrentUserId)
            {
                return Forbid();
            }

            // Constraints 驗證：摘要長度 >= 30 字
            if (string.IsNullOrWhiteSpace(completeSummary) || completeSummary.Trim().Length < 30)
            {
                TempData["ErrorMessage"] = "完工送審失敗！完工摘要說明需達 30 字以上。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            try
            {
                byte[] rowVersion = Convert.FromBase64String(rowVersionStr);
                var success = await _taskRepository.SubmitToReviewAsync(taskId, completeSummary, CurrentUserId, rowVersion);
                if (success)
                {
                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                    // Notify PM
                    var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                    if (project != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: project.PmUserId,
                            eventType: "ReviewRequested",
                            title: "待同儕審查任務 🔍",
                            message: $"成員 {task.OwnerUserName} 已提交任務「{task.TaskTitle}」的完工審查，請協助審查附件與摘要。完工摘要：{completeSummary}",
                            refType: "TASK",
                            refId: taskId
                        );
                    }

                    // Notify Assistants
                    if (task.AssistUserIds != null)
                    {
                        foreach (var assistantId in task.AssistUserIds)
                        {
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: assistantId,
                                eventType: "ReviewRequested",
                                title: "待同儕審查任務 🔍",
                                message: $"成員 {task.OwnerUserName} 已提交任務「{task.TaskTitle}」的完工審查，請協助審查附件與摘要。完工摘要：{completeSummary}",
                                refType: "TASK",
                                refId: taskId
                            );
                        }
                    }

                    TempData["SuccessMessage"] = "任務已成功提交送審！狀態已變更為「審查中」。";
                }
                else
                {
                    TempData["ErrorMessage"] = "送審失敗！請確認該任務底下是否已有至少一個有效附件。";
                }
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"送審失敗：{ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id = taskId });
        }

        // REV-002: 同儕審查與退回 (REV-003)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessReview(int taskId, int reviewId, string reviewResult, string reviewComment, string taskRowVersionStr, string reviewRowVersionStr)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return NotFound();

            // REV-002: 審查人不得為該任務負責人
            if (task.OwnerUserId == CurrentUserId)
            {
                TempData["ErrorMessage"] = "審查失敗！您不能審查自己負責的任務。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            if (string.IsNullOrWhiteSpace(reviewComment))
            {
                TempData["ErrorMessage"] = "審查意見或退回原因不可為空！";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            try
            {
                byte[] reviewRowVersion = Convert.FromBase64String(reviewRowVersionStr);
                byte[] taskRowVersion = Convert.FromBase64String(taskRowVersionStr);

                var review = new TaskReview
                {
                    ReviewId = reviewId,
                    TaskId = taskId,
                    ReviewerUserId = CurrentUserId,
                    ReviewResult = reviewResult, // Approved, Rejected
                    ReviewComment = reviewComment,
                    SourceType = "PeerReview",
                    RowVersion = reviewRowVersion
                };

                var success = await _taskRepository.ProcessReviewAsync(review, taskRowVersion, CurrentUserId);
                if (success)
                {
                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                    var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                    var reviewerName = User.FindFirst(ClaimTypes.Name)?.Value ?? "審查人";

                    if (reviewResult == "Approved")
                    {
                        // Notify Owner (In-app only)
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: task.OwnerUserId,
                            eventType: "ReviewApproved",
                            title: "任務審查通過 🎉",
                            message: $"您的任務「{task.TaskTitle}」已由 {reviewerName} 審查通過，狀態已更新為已完成。",
                            refType: "TASK",
                            refId: taskId,
                            sendEmail: false
                        );

                        // Notify PM (In-app only)
                        if (project != null)
                        {
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: project.PmUserId,
                                eventType: "ReviewApproved",
                                title: "任務審查通過 🎉",
                                message: $"任務「{task.TaskTitle}」（負責人：{task.OwnerUserName}）已由 {reviewerName} 審查通過，狀態已更新為已完成。",
                                refType: "TASK",
                                refId: taskId,
                                sendEmail: false
                            );
                        }

                        TempData["SuccessMessage"] = "審查通過！任務狀態已更新為「已完成」。";
                    }
                    else
                    {
                        // Notify Owner (In-app + Email)
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: task.OwnerUserId,
                            eventType: "ReviewRejected",
                            title: "任務審查退回 ❌",
                            message: $"您的任務「{task.TaskTitle}」未通過 {reviewerName} 的同儕審查，已退回至進行中。退回原因：{reviewComment}",
                            refType: "TASK",
                            refId: taskId,
                            sendEmail: true
                        );

                        // Notify PM (In-app + Email)
                        if (project != null)
                        {
                            await _notificationService.SendNotificationAsync(
                                receiverUserId: project.PmUserId,
                                eventType: "ReviewRejected",
                                title: "任務審查退回 ❌",
                                message: $"任務「{task.TaskTitle}」（負責人：{task.OwnerUserName}）未通過 {reviewerName} 的同儕審查，已退回至進行中。退回原因：{reviewComment}",
                                refType: "TASK",
                                refId: taskId,
                                sendEmail: true
                            );
                        }

                        TempData["SuccessMessage"] = "審查已退回！任務已退回「進行中」，並發送退回通知。";
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = "審查處理失敗。";
                }
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"審查發生錯誤：{ex.Message}";
            }

            string? returnUrl = Request.Form["ReturnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Details), new { id = taskId });
        }

        // TSK-DEL: 刪除任務 (僅限 Admin / PM)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTask(int taskId)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null)
            {
                TempData["ErrorMessage"] = "找不到該任務。";
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            int projectId = task.ProjectId;

            try
            {
                var success = await _taskRepository.DeleteTaskAsync(taskId);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "DeleteTask",
                        "MPMS_TASK",
                        taskId.ToString(),
                        task,
                        null
                    );
                    TempData["SuccessMessage"] = $"任務「{task.TaskTitle}」已成功刪除。";
                }
                else
                {
                    TempData["ErrorMessage"] = "刪除任務失敗，請稍後再試。";
                    return RedirectToAction(nameof(Details), new { id = taskId });
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "刪除任務失敗");
                return RedirectToAction(nameof(Details), new { id = taskId });
            }

            return RedirectToAction("Phases", "Project", new { projectId });
        }
    }
}
