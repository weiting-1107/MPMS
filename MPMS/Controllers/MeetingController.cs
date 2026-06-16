using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MPMS.Hubs;
using MPMS.Models;
using MPMS.Repositories;
using MPMS.Services;

namespace MPMS.Controllers
{
    [Authorize]
    public class MeetingController : Controller
    {
        private readonly MeetingRepository _meetingRepository;
        private readonly ProjectRepository _projectRepository;
        private readonly UserRepository _userRepository;
        private readonly TaskRepository _taskRepository;
        private readonly ProjectPhaseRepository _projectPhaseRepository;
        private readonly SnapshotRepository _snapshotRepository;
        private readonly AuditService _auditService;
        private readonly IHubContext<MeetingHub> _hubContext;
        private readonly NotificationService _notificationService;
        private readonly BlockRepository _blockRepository;

        public MeetingController(
            MeetingRepository meetingRepository,
            ProjectRepository projectRepository,
            UserRepository userRepository,
            TaskRepository taskRepository,
            ProjectPhaseRepository projectPhaseRepository,
            SnapshotRepository snapshotRepository,
            AuditService auditService,
            IHubContext<MeetingHub> hubContext,
            NotificationService notificationService,
            BlockRepository blockRepository)
        {
            _meetingRepository = meetingRepository;
            _projectRepository = projectRepository;
            _userRepository = userRepository;
            _taskRepository = taskRepository;
            _projectPhaseRepository = projectPhaseRepository;
            _snapshotRepository = snapshotRepository;
            _auditService = auditService;
            _hubContext = hubContext;
            _notificationService = notificationService;
            _blockRepository = blockRepository;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // GET: Meeting/ListMeetings
        [HttpGet]
        public async Task<IActionResult> ListMeetings()
        {
            var meetings = await _meetingRepository.GetMeetingsAsync();
            var projects = await _projectRepository.GetProjectsAsync();
            var activeUsers = await _taskRepository.GetActiveUsersAsync();

            ViewBag.Projects = projects;
            ViewBag.Users = activeUsers;

            return View(meetings);
        }

        // POST: Meeting/Create
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Meeting model)
        {
            if (string.IsNullOrWhiteSpace(model.MeetingWeek))
            {
                model.MeetingWeek = CalculateIso8601Week(model.MeetingDate);
            }

            model.CreatedAt = DateTime.Now;

            if (ModelState.IsValid)
            {
                var meetingId = await _meetingRepository.CreateMeetingAsync(model);
                if (meetingId > 0)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "CreateMeeting",
                        "MPMS_MEETING",
                        meetingId.ToString(),
                        null,
                        model
                    );
                    TempData["SuccessMessage"] = "成功建立週會！";
                    return RedirectToAction(nameof(Index), new { id = meetingId });
                }
                ModelState.AddModelError(string.Empty, "建立週會失敗。");
            }

            // If invalid, redirect back with error
            TempData["ErrorMessage"] = "建立週會失敗！請確認所有必填欄位。";
            return RedirectToAction(nameof(ListMeetings));
        }

        // GET: Meeting/Index/5 (週會控制台)
        [HttpGet]
        public async Task<IActionResult> Index(int id)
        {
            var meeting = await _meetingRepository.GetMeetingByIdAsync(id);
            if (meeting == null) return NotFound();

            var (startOfWeek, endOfWeek) = GetWeekDateRange(meeting.MeetingDate);

            // Fetch focal lists
            var tasksDue = await _meetingRepository.GetTasksDueThisWeekAsync(startOfWeek, endOfWeek, meeting.ProjectId);
            var tasksUpdated = await _meetingRepository.GetTasksUpdatedThisWeekAsync(startOfWeek, endOfWeek, meeting.ProjectId);
            var allBlocks = await _blockRepository.GetBlockLogsAsync(projectId: meeting.ProjectId);
            var tasksBlocked = allBlocks.Where(b => b.BlockStatus == "Open" || b.BlockStatus == "InProgress" || b.BlockStatus == "WaitingExternal").ToList();
            var tasksReviewing = await _meetingRepository.GetReviewingTasksAsync(meeting.ProjectId);
            var tasksCompleted = await _meetingRepository.GetTasksCompletedThisWeekAsync(startOfWeek, endOfWeek, meeting.ProjectId);
            var attachments = await _meetingRepository.GetAttachmentWallAsync(startOfWeek, endOfWeek, meeting.ProjectId);
            var actionItems = await _meetingRepository.GetMeetingActionsAsync(meeting.MeetingId);
            var focalTasks = await _meetingRepository.GetTasksForThreeWeeksAsync(startOfWeek, endOfWeek, meeting.ProjectId);

            var activeUsers = await _taskRepository.GetActiveUsersAsync();
            var allProjects = await _projectRepository.GetProjectsAsync();

            ViewBag.StartOfWeek = startOfWeek;
            ViewBag.EndOfWeek = endOfWeek;

            ViewBag.TasksDue = tasksDue;
            ViewBag.TasksUpdated = tasksUpdated;
            ViewBag.TasksBlocked = tasksBlocked;
            ViewBag.TasksReviewing = tasksReviewing;
            ViewBag.TasksCompleted = tasksCompleted;
            ViewBag.Attachments = attachments;
            ViewBag.ActionItems = actionItems;
            ViewBag.FocalTasks = focalTasks;

            ViewBag.Users = activeUsers;
            ViewBag.AllProjects = allProjects;

            // If the meeting is associated with a project, pre-load phases for Action Item task conversion
            if (meeting.ProjectId.HasValue)
            {
                ViewBag.Phases = await _projectPhaseRepository.GetPhasesByProjectIdAsync(meeting.ProjectId.Value);
            }
            else
            {
                ViewBag.Phases = Enumerable.Empty<ProjectPhase>();
            }

            // Snapshot status check
            var snapshotExists = await _snapshotRepository.CheckSnapshotExistsAsync(id);
            var latestVersion = await _snapshotRepository.GetLatestSnapshotVersionAsync(id);
            ViewBag.SnapshotExists = snapshotExists;
            ViewBag.SnapshotVersion = latestVersion;

            return View(meeting);
        }

        // POST: Meeting/UpdateMeetingNote
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> UpdateMeetingNote(int meetingId, string? meetingNote)
        {
            var success = await _meetingRepository.UpdateMeetingNoteAsync(meetingId, meetingNote);
            if (success)
            {
                await _hubContext.Clients.All.SendAsync("MeetingNoteUpdated", meetingId, meetingNote);
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "儲存會議記錄失敗。" });
        }

        // POST: Meeting/AddActionItem
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddActionItem(MeetingAction model)
        {
            if (ModelState.IsValid)
            {
                var success = await _meetingRepository.CreateMeetingActionAsync(model);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "AddMeetingAction",
                        "MPMS_MEETING_ACTION",
                        model.ActionTitle,
                        null,
                        model
                    );

                    // Notify Action Item Owner
                    await _notificationService.SendNotificationAsync(
                        receiverUserId: model.OwnerUserId,
                        eventType: "NewActionItem",
                        title: "會議臨時派工 📝",
                        message: $"您在週會中被指派了臨時任務「{model.ActionTitle}」，截止日期：{model.DueDate:yyyy-MM-dd}。",
                        refType: "MEETING",
                        refId: model.MeetingId
                    );

                    await _hubContext.Clients.All.SendAsync("ActionItemsUpdated", model.MeetingId);
                    TempData["SuccessMessage"] = "成功指派臨時任務 (Action Item)！";
                }
                else
                {
                    TempData["ErrorMessage"] = "新增臨時任務失敗。";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "資料驗證失敗，請確認欄位是否填寫正確。";
            }

            return RedirectToAction(nameof(Index), new { id = model.MeetingId });
        }

        // POST: Meeting/ConvertActionToTask
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConvertActionToTask(int actionId, int meetingId, int phaseId, string priority)
        {
            if (phaseId <= 0)
            {
                TempData["ErrorMessage"] = "轉換失敗！請選擇要關聯的專案階段。";
                return RedirectToAction(nameof(Index), new { id = meetingId });
            }

            var taskId = await _meetingRepository.ConvertActionToTaskAsync(actionId, phaseId, priority, CurrentUserId);
            if (taskId > 0)
            {
                await _auditService.LogAsync(
                    CurrentUserId,
                    "ConvertActionToTask",
                    "MPMS_MEETING_ACTION",
                    actionId.ToString(),
                    new { action_id = actionId },
                    new { action_id = actionId, task_id = taskId, phase_id = phaseId }
                );

                // Notify Task Owner about the conversion
                var task = await _taskRepository.GetTaskByIdAsync(taskId);
                if (task != null)
                {
                    await _notificationService.SendNotificationAsync(
                        receiverUserId: task.OwnerUserId,
                        eventType: "NewTask",
                        title: "臨時任務轉為正式任務 📋",
                        message: $"您的臨時任務已轉為正式專案任務「{task.TaskTitle}」，截止日期：{task.DueDate:yyyy-MM-dd}。",
                        refType: "TASK",
                        refId: taskId
                    );
                }

                // Broadcast SignalR updates
                await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);
                await _hubContext.Clients.All.SendAsync("ActionItemsUpdated", meetingId);

                TempData["SuccessMessage"] = "已成功將臨時任務轉為正式專案任務！";
            }
            else
            {
                TempData["ErrorMessage"] = "臨時任務轉換失敗。";
            }

            return RedirectToAction(nameof(Index), new { id = meetingId });
        }

        // POST: Meeting/MeetingReturnTask (MTG-003: 現場退回)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MeetingReturnTask(int taskId, int meetingId, string reason, string rowVersionStr)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "退回失敗！必須填寫現場退回原因。";
                return RedirectToAction(nameof(Index), new { id = meetingId });
            }

            try
            {
                byte[] rowVersion = Convert.FromBase64String(rowVersionStr);
                var success = await _taskRepository.MeetingReturnTaskAsync(taskId, reason, CurrentUserId, rowVersion);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "MeetingReturnTask",
                        "MPMS_TASK",
                        taskId.ToString(),
                        null,
                        new { task_id = taskId, meeting_id = meetingId, return_reason = reason }
                    );

                    // Notify Task Owner
                    var task = await _taskRepository.GetTaskByIdAsync(taskId);
                    if (task != null)
                    {
                        var reviewerName = User.FindFirst(ClaimTypes.Name)?.Value ?? "專案經理";
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: task.OwnerUserId,
                            eventType: "ReviewRejected",
                            title: "週會任務現場退回 ⚠️",
                            message: $"您的任務「{task.TaskTitle}」在週會現場被 {reviewerName} 退回為進行中。原因：{reason}",
                            refType: "TASK",
                            refId: taskId,
                            sendEmail: true
                        );
                    }

                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                    TempData["SuccessMessage"] = "任務已現場退回至「進行中」！";
                }
                else
                {
                    TempData["ErrorMessage"] = "現場退回操作失敗。";
                }
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "現場退回發生錯誤");
            }

            return RedirectToAction(nameof(Index), new { id = meetingId });
        }

        // POST: Meeting/SealSnapshot (SNP-001 / SNP-002: 快照封存與修正版)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SealSnapshot(int meetingId, string? revisionReason)
        {
            var meeting = await _meetingRepository.GetMeetingByIdAsync(meetingId);
            if (meeting == null) return NotFound();

            var hasExisting = await _snapshotRepository.CheckSnapshotExistsAsync(meetingId);
            if (hasExisting && string.IsNullOrWhiteSpace(revisionReason))
            {
                TempData["ErrorMessage"] = "建立修正版快照失敗！必須填寫修正原因。";
                return RedirectToAction(nameof(Index), new { id = meetingId });
            }

            try
            {
                var targetProjects = new System.Collections.Generic.List<Project>();
                if (meeting.ProjectId.HasValue)
                {
                    var p = await _projectRepository.GetProjectByIdAsync(meeting.ProjectId.Value);
                    if (p != null) targetProjects.Add(p);
                }
                else
                {
                    var allP = await _projectRepository.GetProjectsAsync();
                    targetProjects.AddRange(allP);
                }

                if (!targetProjects.Any())
                {
                    TempData["ErrorMessage"] = "目前系統中沒有可封存的專案。";
                    return RedirectToAction(nameof(Index), new { id = meetingId });
                }

                int sealedCount = 0;
                foreach (var project in targetProjects)
                {
                    var snapshotId = await _snapshotRepository.SealSnapshotAsync(
                        meetingId,
                        project.ProjectId,
                        CurrentUserId,
                        revisionReason,
                        meetingDate: meeting.MeetingDate  // 用週會日期決定 week_code
                    );

                    if (snapshotId > 0)
                    {
                        sealedCount++;
                        var projVersion = await _snapshotRepository.GetLatestSnapshotVersionAsync(meetingId);

                        await _auditService.LogAsync(
                            CurrentUserId,
                            "SealSnapshot",
                            "MPMS_WEEKLY_SNAPSHOT",
                            snapshotId.ToString(),
                            null,
                            new { meeting_id = meetingId, snapshot_id = snapshotId, project_id = project.ProjectId, is_revision = hasExisting }
                        );
                    }
                }

                if (sealedCount > 0)
                {
                    var version = await _snapshotRepository.GetLatestSnapshotVersionAsync(meetingId);

                    // Send notifications (In-app only)
                    var activeUsers = await _taskRepository.GetActiveUsersAsync();
                    var notifyUserIds = new System.Collections.Generic.HashSet<int>();
                    notifyUserIds.Add(CurrentUserId);
                    
                    if (meeting.ProjectId.HasValue)
                    {
                        notifyUserIds.Add(targetProjects.First().PmUserId);
                    }
                    else
                    {
                        foreach (var p in targetProjects) notifyUserIds.Add(p.PmUserId);
                    }

                    foreach (var u in activeUsers)
                    {
                        if (u.RoleCode == "ADMIN")
                        {
                            notifyUserIds.Add(u.UserId);
                        }
                    }

                    var snapshotTitle = "週進度快照封存完成 📦";
                    var snapshotMsg = hasExisting
                        ? $"週會（週別：{meeting.MeetingWeek}）之專案進度與統計數據已封存為修正版 (v{version})。修正原因：{revisionReason}"
                        : $"週會（週別：{meeting.MeetingWeek}）之專案進度與統計數據已封存完成 (v{version})。";

                    foreach (var receiverId in notifyUserIds)
                    {
                        await _notificationService.SendNotificationAsync(
                            receiverUserId: receiverId,
                            eventType: "SealSnapshot",
                            title: snapshotTitle,
                            message: snapshotMsg,
                            refType: "MEETING",
                            refId: meetingId,
                            sendEmail: false
                        );
                    }

                    TempData["SuccessMessage"] = hasExisting
                        ? $"已成功為 {sealedCount} 個專案封存週快照修正版 (v{version})！"
                        : $"已成功為 {sealedCount} 個專案封存當週週會快照！";
                }
                else
                {
                    TempData["ErrorMessage"] = "快照封存失敗，無任何專案被處理。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "快照封存發生錯誤");
            }

            return RedirectToAction(nameof(Index), new { id = meetingId });
        }

        // GET: Meeting/Trends (RPT-001: 歷史趨勢圖)
        [HttpGet]
        public async Task<IActionResult> Trends()
        {
            var projects = await _projectRepository.GetProjectsAsync();
            return View(projects);
        }

        // GET: Meeting/GetTrendData?projectId=3 (RPT-001: 歷史趨勢圖 JSON API)
        [HttpGet]
        public async Task<IActionResult> GetTrendData(int? projectId)
        {
            if (!projectId.HasValue)
            {
                return Json(new { success = false, message = "未指定專案。" });
            }

            var progressData = await _snapshotRepository.GetProjectProgressTrendAsync(projectId.Value);
            var blockedData = await _snapshotRepository.GetBlockedTasksTrendAsync(projectId.Value);

            // Group by Week and align the datasets
            var progressList = progressData.ToList();
            var blockedList = blockedData.ToList();

            // Extract labels (weeks) from both datasets to form a union list
            var weeks = progressList.Select(x => (string)x.Week)
                                    .Union(blockedList.Select(x => (string)x.Week))
                                    .OrderBy(w => w)
                                    .ToList();

            var progressValues = new List<decimal>();
            var blockedCounts = new List<int>();

            foreach (var week in weeks)
            {
                var pVal = progressList.FirstOrDefault(x => x.Week == week);
                progressValues.Add(pVal != null ? (decimal)pVal.Progress : 0.00m);

                var bCount = blockedList.FirstOrDefault(x => x.Week == week);
                blockedCounts.Add(bCount != null ? (int)bCount.BlockedCount : 0);
            }

            return Json(new
            {
                success = true,
                weeks = weeks,
                progress = progressValues,
                blocked = blockedCounts
            });
        }

        // GET: Meeting/GetPhases?projectId=5
        [HttpGet]
        public async Task<IActionResult> GetPhases(int projectId)
        {
            var phases = await _projectPhaseRepository.GetPhasesByProjectIdAsync(projectId);
            return Json(phases.Select(p => new { phaseId = p.PhaseId, phaseName = p.PhaseName }));
        }

        // POST: Meeting/DeleteMeeting
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMeeting(int meetingId)
        {
            var meeting = await _meetingRepository.GetMeetingByIdAsync(meetingId);
            if (meeting == null)
            {
                TempData["ErrorMessage"] = "找不到該週會記錄。";
                return RedirectToAction(nameof(ListMeetings));
            }

            try
            {
                var success = await _meetingRepository.DeleteMeetingAsync(meetingId);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "DeleteMeeting",
                        "MPMS_MEETING",
                        meetingId.ToString(),
                        meeting,
                        null
                    );
                    TempData["SuccessMessage"] = $"週會「{meeting.MeetingWeek}」已成功刪除。";
                }
                else
                {
                    TempData["ErrorMessage"] = "刪除週會失敗，請稍後再試。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "刪除週會失敗");
            }

            return RedirectToAction(nameof(ListMeetings));
        }

        // ==================== Helper Methods ====================

        private string CalculateIso8601Week(DateTime date)
        {
            DayOfWeek day = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(date);
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
            {
                date = date.AddDays(3);
            }
            int week = System.Globalization.CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return $"{date.Year}-W{week:D2}";
        }

        private (DateTime start, DateTime end) GetWeekDateRange(DateTime date)
        {
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            DateTime start = date.AddDays(-1 * diff).Date;
            DateTime end = start.AddDays(6).Date;
            return (start, end);
        }
    }
}
