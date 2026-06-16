using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MPMS.Models;
using MPMS.Repositories;
using MPMS.Services;

namespace MPMS.Controllers
{
    [Authorize]
    public class BlockController : Controller
    {
        private readonly BlockRepository _blockRepository;
        private readonly TaskRepository _taskRepository;
        private readonly ProjectRepository _projectRepository;
        private readonly ProjectPhaseRepository _projectPhaseRepository;
        private readonly NotificationService _notificationService;
        private readonly AuditService _auditService;
        private readonly IHubContext<MPMS.Hubs.MeetingHub> _meetingHubContext;
        private readonly string _connectionString;

        public BlockController(
            BlockRepository blockRepository,
            TaskRepository taskRepository,
            ProjectRepository projectRepository,
            ProjectPhaseRepository projectPhaseRepository,
            NotificationService notificationService,
            AuditService auditService,
            IConfiguration configuration,
            IHubContext<MPMS.Hubs.MeetingHub> meetingHubContext)
        {
            _blockRepository = blockRepository;
            _taskRepository = taskRepository;
            _projectRepository = projectRepository;
            _projectPhaseRepository = projectPhaseRepository;
            _notificationService = notificationService;
            _auditService = auditService;
            _meetingHubContext = meetingHubContext;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is missing in appsettings.json");
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        private string CurrentUserName => User.FindFirst(ClaimTypes.Name)?.Value ?? "系統使用者";

        // BLK002: PM 卡關處理中心頁面 (限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Index(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            string? blockType = null,
            string? impactLevel = null,
            bool? isMilestone = null,
            string? blockStatus = "Open", // 預設顯示未處理的
            bool? isWeeklyFocus = null,
            bool? isOverdue = null)
        {
            var logs = await _blockRepository.GetBlockLogsAsync(
                projectId, phaseId, ownerUserId, blockType, impactLevel, isMilestone, blockStatus, isWeeklyFocus, isOverdue);

            // 獲取待同儕審查的任務
            var reviewingTasks = (await _blockRepository.GetReviewingTasksWithFiltersAsync(
                projectId, phaseId, ownerUserId, isMilestone)).ToList();

            if (reviewingTasks.Any())
            {
                var taskIds = reviewingTasks.Select(t => t.TaskId).Distinct().ToList();
                var allAttachments = await _blockRepository.GetAttachmentsForTasksAsync(taskIds);
                foreach (var task in reviewingTasks)
                {
                    task.Attachments = allAttachments.Where(a => a.TaskId == task.TaskId).ToList();
                }
            }

            ViewBag.ReviewingTasks = reviewingTasks;
            ViewBag.ReviewingTasksCount = reviewingTasks.Count;

            // Fetch dropdown elements for filter bar
            var projects = await _projectRepository.GetProjectsAsync();
            var activeUsers = await _taskRepository.GetActiveUsersAsync();

            ViewBag.Projects = projects;
            ViewBag.Users = activeUsers;
            ViewBag.CurrentProjectId = projectId;
            ViewBag.CurrentPhaseId = phaseId;
            ViewBag.CurrentOwnerUserId = ownerUserId;
            ViewBag.CurrentBlockType = blockType;
            ViewBag.CurrentImpactLevel = impactLevel;
            ViewBag.CurrentIsMilestone = isMilestone;
            ViewBag.CurrentBlockStatus = blockStatus;
            ViewBag.CurrentIsWeeklyFocus = isWeeklyFocus;
            ViewBag.CurrentIsOverdue = isOverdue;

            if (projectId.HasValue)
            {
                var phases = await _projectPhaseRepository.GetPhasesByProjectIdAsync(projectId.Value);
                ViewBag.Phases = phases;
            }
            else
            {
                ViewBag.Phases = new List<ProjectPhase>();
            }

            return View(logs.ToList());
        }

        // BLK001: 申報卡關 (Post)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReportBlock(int taskId, string blockType, string blockReason, string helpNeeded, string impactLevel, string taskRowVersionStr)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return NotFound("找不到該任務。");

            // 權限檢查：負責人、協辦人、PM、Admin 可申報
            var isUserAdmin = User.IsInRole("ADMIN");
            var isUserPm = User.IsInRole("PM");
            var isOwner = task.OwnerUserId == CurrentUserId;
            var isAssistant = task.AssistUserIds.Contains(CurrentUserId);

            if (!isUserAdmin && !isUserPm && !isOwner && !isAssistant)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(blockReason) || string.IsNullOrWhiteSpace(helpNeeded))
            {
                TempData["ErrorMessage"] = "申報卡關時，必須填寫「卡關原因」與「需要協助事項」！";
                return RedirectToAction("Details", "Task", new { id = taskId });
            }

            // 檢查是否已有活躍卡關 (防重複申報)
            var activeBlock = await _blockRepository.GetActiveBlockLogByTaskIdAsync(taskId);
            if (activeBlock != null)
            {
                TempData["ErrorMessage"] = "該任務目前已處於卡關狀態，不可重複申報卡關。";
                return RedirectToAction("Details", "Task", new { id = taskId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();

            try
            {
                byte[] taskRowVersion = Convert.FromBase64String(taskRowVersionStr);
                var utcNow = DateTime.UtcNow;

                // 1. 建立 Block Log 紀錄
                var blockLog = new TaskBlockLog
                {
                    TaskId = taskId,
                    BlockType = blockType,
                    BlockReason = blockReason,
                    HelpNeeded = helpNeeded,
                    ImpactLevel = impactLevel,
                    BlockedBy = CurrentUserId,
                    BlockedAtUtc = utcNow,
                    BlockStatus = "Open",
                    IsWeeklyFocus = false,
                    CreatedAtUtc = utcNow
                };

                int blockId = await _blockRepository.CreateBlockLogAsync(blockLog, conn, trans);
                blockLog.BlockId = blockId;

                // 2. 如果任務原先是 Reviewing (審查中)，則將當前的 Pending Review 改為 Cancelled
                if (task.TaskStatus == "Reviewing" && task.CurrentReviewId.HasValue)
                {
                    const string sqlCancelReview = @"
                        UPDATE dbo.MPMS_TASK_REVIEW
                        SET review_result = 'Cancelled',
                            reviewed_at = GETDATE(),
                            review_comment = @Reason
                        WHERE review_id = @CurrentReviewId";
                    await conn.ExecuteAsync(sqlCancelReview, new 
                    { 
                        CurrentReviewId = task.CurrentReviewId.Value, 
                        Reason = $"[審查中卡關] {blockReason}" 
                    }, transaction: trans);
                }

                // 3. 更新 Task 狀態與關聯 ID
                var updateSuccess = await _taskRepository.UpdateTaskBlockStatusAsync(
                    taskId, "Blocked", blockId, blockReason, CurrentUserId, taskRowVersion, conn, trans);
                
                if (!updateSuccess)
                {
                    throw new System.Data.DBConcurrencyException("任務資料已被他人修改，請重新整理頁面。");
                }

                // 4. 寫入 Task Status Log
                const string sqlStatusLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, @OldStatus, 'Blocked', @Reason, @OperatorUserId, GETDATE())";
                await conn.ExecuteAsync(sqlStatusLog, new 
                { 
                    TaskId = taskId, 
                    OldStatus = task.TaskStatus, 
                    Reason = $"[卡關申報] {blockReason}", 
                    OperatorUserId = CurrentUserId 
                }, transaction: trans);

                // 5. 寫入 Block Comment (作為狀態變更紀錄)
                var blockComment = new TaskBlockComment
                {
                    BlockId = blockId,
                    CommentType = "StatusChange",
                    CommentText = $"[卡關申報] 成員 {CurrentUserName} 申報卡關。原因：{blockReason}，需要協助事項：{helpNeeded}。",
                    OldBlockStatus = null,
                    NewBlockStatus = "Open",
                    CreatedBy = CurrentUserId,
                    CreatedAtUtc = utcNow
                };
                await _blockRepository.CreateBlockCommentAsync(blockComment, conn, trans);

                // 6. 寫入 Audit Log
                await _auditService.LogAsync(
                    CurrentUserId,
                    "BlockReport",
                    "MPMS_TASK",
                    taskId.ToString(),
                    new { OldStatus = task.TaskStatus, OldBlockId = task.CurrentBlockId },
                    new { NewStatus = "Blocked", NewBlockId = blockId, BlockLog = blockLog },
                    conn,
                    trans
                );

                await trans.CommitAsync();

                // 觸發 SignalR 與 通知
                await _meetingHubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                // 發送通知：通知 PM、負責人、協辦人
                var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                if (project != null)
                {
                    await _notificationService.SendNotificationAsync(
                        receiverUserId: project.PmUserId,
                        eventType: "TaskBlocked",
                        title: "任務卡關申報 🚨",
                        message: $"任務「{task.TaskTitle}」被申報卡關。申報人：{CurrentUserName}。原因：{blockReason}",
                        refType: "TASK",
                        refId: taskId
                    );
                }

                if (task.OwnerUserId != CurrentUserId)
                {
                    await _notificationService.SendNotificationAsync(
                        receiverUserId: task.OwnerUserId,
                        eventType: "TaskBlocked",
                        title: "任務卡關申報 🚨",
                        message: $"您的任務「{task.TaskTitle}」已被申報卡關。原因：{blockReason}",
                        refType: "TASK",
                        refId: taskId
                    );
                }

                foreach (var assistId in task.AssistUserIds)
                {
                    if (assistId != CurrentUserId)
                    {
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: assistId,
                            eventType: "TaskBlocked",
                            title: "任務卡關申報 🚨",
                            message: $"任務「{task.TaskTitle}」（協辦）已被申報卡關。原因：{blockReason}",
                            refType: "TASK",
                            refId: taskId
                        );
                    }
                }

                TempData["SuccessMessage"] = "卡關申報成功！已通知專案 PM。";
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                TempData["ErrorMessage"] = $"卡關申報失敗：{ex.Message}";
            }

            return RedirectToAction("Details", "Task", new { id = taskId });
        }

        // BLK003: PM 卡關處理指派 (Post, 限 PM / Admin)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignHelper(int blockId, int? assignedHelperUserId, DateTime? expectedResolveDate, string? commentText)
        {
            var blockLog = await _blockRepository.GetBlockLogByIdAsync(blockId);
            if (blockLog == null) return NotFound("找不到卡關紀錄。");

            if (blockLog.BlockStatus == "Resolved" || blockLog.BlockStatus == "Cancelled")
            {
                TempData["ErrorMessage"] = "該卡關已解除，不可再變更指派協助人。";
                return RedirectToAction(nameof(Index));
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();

            try
            {
                var utcNow = DateTime.UtcNow;
                var oldBlockLog = new TaskBlockLog
                {
                    AssignedHelperUserId = blockLog.AssignedHelperUserId,
                    ExpectedResolveDate = blockLog.ExpectedResolveDate,
                    BlockStatus = blockLog.BlockStatus
                };

                // 1. 更新卡關紀錄
                blockLog.AssignedHelperUserId = assignedHelperUserId;
                blockLog.ExpectedResolveDate = expectedResolveDate;
                blockLog.BlockStatus = "InProgress"; // 開始處理
                blockLog.UpdatedAtUtc = utcNow;

                await _blockRepository.UpdateBlockLogAsync(blockLog, conn, trans);

                // 2. 建立評論與歷程
                string helperNameText = "無";
                if (assignedHelperUserId.HasValue)
                {
                    const string sqlUser = "SELECT user_name FROM dbo.MPMS_USER WHERE user_id = @UserId";
                    helperNameText = await conn.QueryFirstOrDefaultAsync<string>(sqlUser, new { UserId = assignedHelperUserId.Value }, transaction: trans) ?? "未知人員";
                }

                string assignmentDesc = $"[卡關指派] PM {CurrentUserName} 指派協助人：{helperNameText}，預計解除日：{(expectedResolveDate.HasValue ? expectedResolveDate.Value.ToString("yyyy-MM-dd") : "未設定")}。";
                if (!string.IsNullOrWhiteSpace(commentText))
                {
                    assignmentDesc += $" 處理策略：{commentText}";
                }

                var blockComment = new TaskBlockComment
                {
                    BlockId = blockId,
                    CommentType = "Assignment",
                    CommentText = assignmentDesc,
                    OldBlockStatus = oldBlockLog.BlockStatus,
                    NewBlockStatus = "InProgress",
                    CreatedBy = CurrentUserId,
                    CreatedAtUtc = utcNow
                };
                await _blockRepository.CreateBlockCommentAsync(blockComment, conn, trans);

                // 3. 寫入 Audit Log
                await _auditService.LogAsync(
                    CurrentUserId,
                    "BlockAssign",
                    "MPMS_TASK_BLOCK_LOG",
                    blockId.ToString(),
                    oldBlockLog,
                    new { AssignedHelperUserId = assignedHelperUserId, ExpectedResolveDate = expectedResolveDate, BlockStatus = "InProgress" },
                    conn,
                    trans
                );

                await trans.CommitAsync();

                // SignalR
                await _meetingHubContext.Clients.All.SendAsync("TaskUpdated", blockLog.TaskId);

                // 發送通知：通知協助人、負責人、PM
                if (assignedHelperUserId.HasValue)
                {
                    await _notificationService.SendNotificationAsync(
                        receiverUserId: assignedHelperUserId.Value,
                        eventType: "BlockAssigned",
                        title: "卡關處理指派 🤝",
                        message: $"PM {CurrentUserName} 指派您協助處理任務「{blockLog.TaskTitle}」的卡關。需要協助事項：{blockLog.HelpNeeded}",
                        refType: "TASK",
                        refId: blockLog.TaskId
                    );
                }

                await _notificationService.SendNotificationAsync(
                    receiverUserId: blockLog.OwnerUserId,
                    eventType: "BlockAssigned",
                    title: "卡關處理更新 🤝",
                    message: $"您的卡關任務「{blockLog.TaskTitle}」已由 PM 指派協助人：{helperNameText}，預計解除日為：{(expectedResolveDate.HasValue ? expectedResolveDate.Value.ToString("yyyy-MM-dd") : "未設定")}。",
                    refType: "TASK",
                    refId: blockLog.TaskId
                );

                TempData["SuccessMessage"] = "已成功指派協助人與更新解除日期！";
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                TempData["ErrorMessage"] = $"卡關指派失敗：{ex.Message}";
            }

            // 若是從詳情頁過來的
            string? returnUrl = Request.Form["ReturnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        // BLK004: 卡關處理紀錄/留言 (Post)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(int blockId, string commentText)
        {
            var blockLog = await _blockRepository.GetBlockLogByIdAsync(blockId);
            if (blockLog == null) return NotFound("找不到卡關紀錄。");

            if (string.IsNullOrWhiteSpace(commentText))
            {
                TempData["ErrorMessage"] = "留言內容不能為空！";
                return RedirectToAction("Details", "Task", new { id = blockLog.TaskId });
            }

            try
            {
                var utcNow = DateTime.UtcNow;
                var comment = new TaskBlockComment
                {
                    BlockId = blockId,
                    CommentType = "Comment",
                    CommentText = commentText,
                    OldBlockStatus = blockLog.BlockStatus,
                    NewBlockStatus = blockLog.BlockStatus,
                    CreatedBy = CurrentUserId,
                    CreatedAtUtc = utcNow
                };

                await _blockRepository.CreateBlockCommentAsync(comment);

                // 更新 block_log 的更新時間
                blockLog.UpdatedAtUtc = utcNow;
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await _blockRepository.UpdateBlockLogAsync(blockLog, conn);

                // 發送通知給相關人員 (負責人、協助人、PM)
                var project = await _projectRepository.GetProjectByIdAsync(blockLog.ProjectId);
                var notifyUsers = new HashSet<int> { blockLog.OwnerUserId };
                if (blockLog.AssignedHelperUserId.HasValue) notifyUsers.Add(blockLog.AssignedHelperUserId.Value);
                if (project != null) notifyUsers.Add(project.PmUserId);

                foreach (var userId in notifyUsers)
                {
                    if (userId != CurrentUserId)
                    {
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: userId,
                            eventType: "BlockUpdated",
                            title: "卡關處理更新 💬",
                            message: $"成員 {CurrentUserName} 在任務「{blockLog.TaskTitle}」的卡關係統中留言：{commentText}",
                            refType: "TASK",
                            refId: blockLog.TaskId
                        );
                    }
                }

                TempData["SuccessMessage"] = "處理紀錄留言成功！";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"留言失敗：{ex.Message}";
            }

            string? returnUrl = Request.Form["ReturnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Details", "Task", new { id = blockLog.TaskId });
        }

        // BLK005: 解除卡關 (Post)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveBlock(int blockId, string resolveSummary, string taskRowVersionStr)
        {
            var blockLog = await _blockRepository.GetBlockLogByIdAsync(blockId);
            if (blockLog == null) return NotFound("找不到卡關紀錄。");

            var task = await _taskRepository.GetTaskByIdAsync(blockLog.TaskId);
            if (task == null) return NotFound("找不到關聯任務。");

            // 權限檢查：負責人、PM、Admin 可解除；協辦人需 PM 授權或由 PM 執行
            var isUserAdmin = User.IsInRole("ADMIN");
            var isUserPm = User.IsInRole("PM");
            var isOwner = task.OwnerUserId == CurrentUserId;

            if (!isUserAdmin && !isUserPm && !isOwner)
            {
                TempData["ErrorMessage"] = "無權限解除卡關！僅限任務負責人、專案 PM 或系統管理員解除。";
                return RedirectToAction("Details", "Task", new { id = blockLog.TaskId });
            }

            if (string.IsNullOrWhiteSpace(resolveSummary))
            {
                TempData["ErrorMessage"] = "解除卡關時，必須填寫「解除說明」！";
                return RedirectToAction("Details", "Task", new { id = blockLog.TaskId });
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var trans = await conn.BeginTransactionAsync();

            try
            {
                byte[] taskRowVersion = Convert.FromBase64String(taskRowVersionStr);
                var utcNow = DateTime.UtcNow;

                // 1. 更新 Block Log
                var oldBlockLog = new TaskBlockLog
                {
                    BlockStatus = blockLog.BlockStatus,
                    ResolveSummary = blockLog.ResolveSummary,
                    ResolvedBy = blockLog.ResolvedBy,
                    ResolvedAtUtc = blockLog.ResolvedAtUtc
                };

                blockLog.BlockStatus = "Resolved";
                blockLog.ResolveSummary = resolveSummary;
                blockLog.ResolvedBy = CurrentUserId;
                blockLog.ResolvedAtUtc = utcNow;
                blockLog.UpdatedAtUtc = utcNow;

                await _blockRepository.UpdateBlockLogAsync(blockLog, conn, trans);

                // 2. 更新 Task 狀態 (回到 Progress，current_block_id 設為 NULL)
                var updateSuccess = await _taskRepository.UpdateTaskBlockStatusAsync(
                    blockLog.TaskId, "Progress", null, null, CurrentUserId, taskRowVersion, conn, trans);
                
                if (!updateSuccess)
                {
                    throw new System.Data.DBConcurrencyException("任務資料已被他人修改，請重新整理頁面。");
                }

                // 3. 建立 Comment 歷程
                var blockComment = new TaskBlockComment
                {
                    BlockId = blockId,
                    CommentType = "Resolve",
                    CommentText = $"[卡關解除] 成員 {CurrentUserName} 解除卡關。說明：{resolveSummary}",
                    OldBlockStatus = oldBlockLog.BlockStatus,
                    NewBlockStatus = "Resolved",
                    CreatedBy = CurrentUserId,
                    CreatedAtUtc = utcNow
                };
                await _blockRepository.CreateBlockCommentAsync(blockComment, conn, trans);

                // 4. 寫入 Task Status Log
                const string sqlStatusLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, 'Blocked', 'Progress', @Reason, @OperatorUserId, GETDATE())";
                await conn.ExecuteAsync(sqlStatusLog, new 
                { 
                    TaskId = blockLog.TaskId, 
                    Reason = $"[卡關解除] {resolveSummary}", 
                    OperatorUserId = CurrentUserId 
                }, transaction: trans);

                // 5. 寫入 Audit Log
                await _auditService.LogAsync(
                    CurrentUserId,
                    "BlockResolve",
                    "MPMS_TASK",
                    blockLog.TaskId.ToString(),
                    new { Status = "Blocked", BlockId = blockId },
                    new { Status = "Progress", BlockId = (int?)null, ResolveSummary = resolveSummary },
                    conn,
                    trans
                );

                await trans.CommitAsync();

                // SignalR
                await _meetingHubContext.Clients.All.SendAsync("TaskUpdated", blockLog.TaskId);

                // 發送通知：通知 PM、負責人、協辦人、協助人
                var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
                var notifyUsers = new HashSet<int> { task.OwnerUserId };
                if (blockLog.AssignedHelperUserId.HasValue) notifyUsers.Add(blockLog.AssignedHelperUserId.Value);
                if (project != null) notifyUsers.Add(project.PmUserId);
                foreach (var assistId in task.AssistUserIds) notifyUsers.Add(assistId);

                foreach (var userId in notifyUsers)
                {
                    if (userId != CurrentUserId)
                    {
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: userId,
                            eventType: "BlockResolved",
                            title: "卡關排除解除 🔓",
                            message: $"任務「{task.TaskTitle}」的卡關已被 {CurrentUserName} 解除，任務狀態回到進行中。解除說明：{resolveSummary}",
                            refType: "TASK",
                            refId: task.TaskId
                        );
                    }
                }

                TempData["SuccessMessage"] = "卡關已成功解除！任務狀態已回到「進行中」。";
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                TempData["ErrorMessage"] = $"卡關解除失敗：{ex.Message}";
            }

            string? returnUrl = Request.Form["ReturnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Details", "Task", new { id = blockLog.TaskId });
        }

        // PM 設定卡關任務是否為週會焦點
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleWeeklyFocus(int blockId, bool isWeeklyFocus)
        {
            var blockLog = await _blockRepository.GetBlockLogByIdAsync(blockId);
            if (blockLog == null) return NotFound("找不到卡關紀錄。");

            try
            {
                blockLog.IsWeeklyFocus = isWeeklyFocus;
                blockLog.UpdatedAtUtc = DateTime.UtcNow;

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                var success = await _blockRepository.UpdateBlockLogAsync(blockLog, conn);
                if (success)
                {
                    // 留言記錄
                    var comment = new TaskBlockComment
                    {
                        BlockId = blockId,
                        CommentType = "WeeklyFocus",
                        CommentText = $"[週會焦點設定] PM {CurrentUserName} 將此卡關項目設定為：{(isWeeklyFocus ? "加入週會焦點" : "移出週會焦點")}。",
                        CreatedBy = CurrentUserId,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    await _blockRepository.CreateBlockCommentAsync(comment, conn);

                    await _meetingHubContext.Clients.All.SendAsync("TaskUpdated", blockLog.TaskId);
                    TempData["SuccessMessage"] = isWeeklyFocus ? "已加入週會焦點清單。" : "已從週會焦點清單移出。";
                }
                else
                {
                    TempData["ErrorMessage"] = "設定週會焦點失敗。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"設定週會焦點發生錯誤：{ex.Message}";
            }

            string? returnUrl = Request.Form["ReturnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        // 獲取留言歷程的 JSON 格式 (供 AJAX 呼叫)
        [HttpGet]
        public async Task<IActionResult> GetCommentsJson(int blockId)
        {
            try
            {
                var comments = await _blockRepository.GetCommentsByBlockIdAsync(blockId);
                return Json(comments.Select(c => new
                {
                    c.CommentId,
                    c.BlockId,
                    c.CommentType,
                    c.CommentText,
                    c.OldBlockStatus,
                    c.NewBlockStatus,
                    c.CreatedBy,
                    CreatedAtUtc = c.CreatedAtUtc.ToString("o"), // ISO format
                    c.CreatorName
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ==================== Reports (RPT003 & RPT007) ====================

        // GET: Block/ExportRpt003
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> ExportRpt003(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            string? blockType = null,
            string? impactLevel = null,
            bool? isMilestone = null,
            string? blockStatus = null,
            bool? isWeeklyFocus = null,
            bool? isOverdue = null)
        {
            var logs = await _blockRepository.GetBlockLogsAsync(
                projectId, phaseId, ownerUserId, blockType, impactLevel, isMilestone, blockStatus, isWeeklyFocus, isOverdue);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("專案名稱,專案階段,里程碑,任務名稱,負責人,申報人,卡關類型,卡關原因,協助事項,影響程度,卡關狀態,卡關天數,預計解除日,協助人,排除說明,解除時間");

            foreach (var log in logs)
            {
                var milestoneText = log.IsMilestone ? "是" : "否";
                var typeText = log.BlockType switch
                {
                    "Technical" => "技術問題",
                    "Customer" => "客戶未回覆",
                    "Requirement" => "需求不清",
                    "Permission" => "權限不足",
                    "Data" => "資料不足",
                    "WaitingOthers" => "等待他人",
                    "Resource" => "資源不足",
                    _ => "其他"
                };
                var statusText = log.BlockStatus switch
                {
                    "Open" => "申報開立",
                    "InProgress" => "處理中",
                    "WaitingExternal" => "等待外部",
                    "Resolved" => "已排除",
                    "Cancelled" => "已取消",
                    _ => log.BlockStatus
                };

                sb.AppendLine(string.Join(",",
                    EscapeCsv(log.ProjectName),
                    EscapeCsv(log.PhaseName),
                    EscapeCsv(milestoneText),
                    EscapeCsv(log.TaskTitle),
                    EscapeCsv(log.OwnerUserName),
                    EscapeCsv(log.BlockedByName),
                    EscapeCsv(typeText),
                    EscapeCsv(log.BlockReason),
                    EscapeCsv(log.HelpNeeded),
                    EscapeCsv(log.ImpactLevel),
                    EscapeCsv(statusText),
                    log.BlockedDays,
                    log.ExpectedResolveDate?.ToString("yyyy-MM-dd") ?? "",
                    EscapeCsv(log.HelperName),
                    EscapeCsv(log.ResolveSummary ?? ""),
                    log.ResolvedAtUtc?.AddHours(8).ToString("yyyy-MM-dd HH:mm") ?? ""
                ));
            }

            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"RPT003_卡關任務追蹤報表_{DateTime.Now:yyyyMMddHHmmss}.csv");
        }

        // GET: Block/ExportRpt007
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> ExportRpt007(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            string? blockType = null,
            string? impactLevel = null,
            bool? isMilestone = null,
            string? blockStatus = null,
            bool? isWeeklyFocus = null,
            bool? isOverdue = null)
        {
            var logs = (await _blockRepository.GetBlockLogsAsync(
                projectId, phaseId, ownerUserId, blockType, impactLevel, isMilestone, blockStatus, isWeeklyFocus, isOverdue)).ToList();

            var totalCount = logs.Count;
            var resolvedCount = logs.Count(b => b.BlockStatus == "Resolved");
            var activeCount = logs.Count(b => b.BlockStatus == "Open" || b.BlockStatus == "InProgress" || b.BlockStatus == "WaitingExternal");
            var overdueCount = logs.Count(b => b.BlockStatus != "Resolved" && b.BlockStatus != "Cancelled" && b.ExpectedResolveDate.HasValue && b.ExpectedResolveDate.Value < DateTime.Today);
            var avgResolveDays = logs.Where(b => b.BlockStatus == "Resolved" && b.ResolvedAtUtc.HasValue)
                                     .Select(b => (b.ResolvedAtUtc.Value - b.BlockedAtUtc).TotalDays)
                                     .DefaultIfEmpty(0)
                                     .Average();

            var sb = new System.Text.StringBuilder();
            
            // Section 1: Summary KPI
            sb.AppendLine("一、卡關處理效率總體指標");
            sb.AppendLine($"總申報卡關數,{totalCount}");
            sb.AppendLine($"已排除卡關數,{resolvedCount}");
            sb.AppendLine($"活躍列管中數,{activeCount}");
            sb.AppendLine($"處理逾期未解除數,{overdueCount}");
            sb.AppendLine($"排除平均天數,{avgResolveDays:F1} 天");
            sb.AppendLine();

            // Section 2: By Block Type
            sb.AppendLine("二、卡關問題類型分佈統計");
            sb.AppendLine("卡關類型,卡關次數,百分比");
            var typeGroup = logs.GroupBy(l => l.BlockType).OrderByDescending(g => g.Count());
            foreach (var g in typeGroup)
            {
                var typeText = g.Key switch
                {
                    "Technical" => "技術問題",
                    "Customer" => "客戶未回覆",
                    "Requirement" => "需求不清",
                    "Permission" => "權限不足",
                    "Data" => "資料不足",
                    "WaitingOthers" => "等待他人",
                    "Resource" => "資源不足",
                    _ => "其他"
                };
                double pct = totalCount > 0 ? (double)g.Count() / totalCount * 100 : 0;
                sb.AppendLine($"{typeText},{g.Count()},{pct:F1}%");
            }
            sb.AppendLine();

            // Section 3: By Helper Efficiency
            sb.AppendLine("三、協助人員排除效率排名");
            sb.AppendLine("協助人,指派協助數,已排除數,排除平均天數");
            var helperGroup = logs.Where(l => l.AssignedHelperUserId.HasValue)
                                  .GroupBy(l => l.HelperName)
                                  .OrderByDescending(g => g.Count());
            foreach (var g in helperGroup)
            {
                var helperName = g.Key;
                var assignedCount = g.Count();
                var resolved = g.Count(l => l.BlockStatus == "Resolved");
                var avgDays = g.Where(l => l.BlockStatus == "Resolved" && l.ResolvedAtUtc.HasValue)
                               .Select(l => (l.ResolvedAtUtc.Value - l.BlockedAtUtc).TotalDays)
                               .DefaultIfEmpty(0)
                               .Average();
                sb.AppendLine($"{helperName},{assignedCount},{resolved},{avgDays:F1} 天");
            }
            sb.AppendLine();

            // Section 4: Project Breakdown
            sb.AppendLine("四、各專案卡關排除統計");
            sb.AppendLine("專案名稱,總卡關數,已排除數,平均排除天數");
            var projGroup = logs.GroupBy(l => l.ProjectName).OrderByDescending(g => g.Count());
            foreach (var g in projGroup)
            {
                var projName = g.Key;
                var pCount = g.Count();
                var pResolved = g.Count(l => l.BlockStatus == "Resolved");
                var pAvgDays = g.Where(l => l.BlockStatus == "Resolved" && l.ResolvedAtUtc.HasValue)
                                .Select(l => (l.ResolvedAtUtc.Value - l.BlockedAtUtc).TotalDays)
                                .DefaultIfEmpty(0)
                                .Average();
                sb.AppendLine($"{projName},{pCount},{pResolved},{pAvgDays:F1} 天");
            }

            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"RPT007_卡關處理效率報表_{DateTime.Now:yyyyMMddHHmmss}.csv");
        }

        // GET: Block/PrintRpt003
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> PrintRpt003(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            string? blockType = null,
            string? impactLevel = null,
            bool? isMilestone = null,
            string? blockStatus = null,
            bool? isWeeklyFocus = null,
            bool? isOverdue = null)
        {
            var logs = await _blockRepository.GetBlockLogsAsync(
                projectId, phaseId, ownerUserId, blockType, impactLevel, isMilestone, blockStatus, isWeeklyFocus, isOverdue);

            ViewBag.PrintTime = DateTime.Now;
            return View(logs.ToList());
        }

        // GET: Block/PrintRpt007
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> PrintRpt007(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            string? blockType = null,
            string? impactLevel = null,
            bool? isMilestone = null,
            string? blockStatus = null,
            bool? isWeeklyFocus = null,
            bool? isOverdue = null)
        {
            var logs = (await _blockRepository.GetBlockLogsAsync(
                projectId, phaseId, ownerUserId, blockType, impactLevel, isMilestone, blockStatus, isWeeklyFocus, isOverdue)).ToList();

            ViewBag.PrintTime = DateTime.Now;
            return View(logs);
        }

        private string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
            {
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            }
            return val;
        }
    }
}
