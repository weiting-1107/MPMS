using System;
using System.Collections.Generic;
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
    public class GanttController : Controller
    {
        private readonly ProjectRepository _projectRepository;
        private readonly ProjectPhaseRepository _projectPhaseRepository;
        private readonly TaskRepository _taskRepository;
        private readonly AuditService _auditService;
        private readonly IHubContext<MeetingHub> _hubContext;

        public GanttController(
            ProjectRepository projectRepository,
            ProjectPhaseRepository projectPhaseRepository,
            TaskRepository taskRepository,
            AuditService auditService,
            IHubContext<MeetingHub> hubContext)
        {
            _projectRepository = projectRepository;
            _projectPhaseRepository = projectPhaseRepository;
            _taskRepository = taskRepository;
            _auditService = auditService;
            _hubContext = hubContext;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // GET: Gantt/Index
        [HttpGet]
        public async Task<IActionResult> Index(int projectId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null) return NotFound();

            ViewBag.Project = project;
            return View();
        }

        // GET: Gantt/GetData
        [HttpGet]
        public async Task<IActionResult> GetData(int projectId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null) return NotFound();

            var phases = await _projectPhaseRepository.GetPhasesByProjectIdAsync(projectId);
            var tasks = await _taskRepository.GetTasksByProjectIdAsync(projectId);

            var ganttTasks = new List<object>();

            // 1. Add Phase bars
            foreach (var phase in phases)
            {
                ganttTasks.Add(new
                {
                    id = $"phase_{phase.PhaseId}",
                    name = $"【階段】{phase.PhaseName}",
                    start = phase.StartDate.ToString("yyyy-MM-dd"),
                    end = phase.EndDate.ToString("yyyy-MM-dd"),
                    progress = (int)phase.ManualProgressPct,
                    dependencies = "",
                    custom_class = "gantt-phase-bar",
                    type = "phase",
                    realId = phase.PhaseId
                });
            }

            // 2. Add Task bars
            foreach (var task in tasks)
            {
                // Retrieve full task details (including predecessor IDs)
                var fullTask = await _taskRepository.GetTaskByIdAsync(task.TaskId);
                if (fullTask == null) continue;

                // Determine custom styling class based on status/dates
                var customClassList = new List<string>();
                if (fullTask.IsMilestone)
                {
                    customClassList.Add("gantt-milestone-bar");
                }
                else
                {
                    switch (fullTask.TaskStatus)
                    {
                        case "Blocked":
                            customClassList.Add("gantt-blocked-bar");
                            break;
                        case "Reviewing":
                            customClassList.Add("gantt-reviewing-bar");
                            break;
                        case "Done":
                            customClassList.Add("gantt-done-bar");
                            break;
                        default:
                            customClassList.Add("gantt-default-bar");
                            break;
                    }
                }

                // Check if task is overdue
                if (fullTask.TaskStatus != "Done" && fullTask.DueDate < DateTime.Today)
                {
                    customClassList.Add("gantt-overdue-border");
                }

                // Combine predecessor task IDs as dependency strings for Frappe Gantt
                var dependencyIds = fullTask.PredecessorTaskIds.Select(id => $"task_{id}").ToList();
                // Optionally connect task to its Phase bar as dependency so it groups visually
                // dependencyIds.Insert(0, $"phase_{fullTask.PhaseId}");

                ganttTasks.Add(new
                {
                    id = $"task_{fullTask.TaskId}",
                    name = (fullTask.IsMilestone ? "◆ " : "") + $"{fullTask.TaskTitle} ({fullTask.OwnerUserName})",
                    start = fullTask.PlannedStartDate?.ToString("yyyy-MM-dd") ?? fullTask.DueDate.AddDays(-7).ToString("yyyy-MM-dd"),
                    end = fullTask.DueDate.ToString("yyyy-MM-dd"),
                    progress = fullTask.TaskStatus == "Done" ? 100 : 0,
                    dependencies = string.Join(",", dependencyIds),
                    custom_class = string.Join(" ", customClassList),
                    type = fullTask.IsMilestone ? "milestone" : "task",
                    realId = fullTask.TaskId,
                    status = fullTask.TaskStatus,
                    owner = fullTask.OwnerUserName,
                    rowVersion = fullTask.RowVersion != null ? Convert.ToBase64String(fullTask.RowVersion) : ""
                });
            }

            return Json(ganttTasks);
        }

        // POST: Gantt/UpdateTaskDates
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTaskDates(int taskId, DateTime plannedStartDate, DateTime dueDate, string rowVersionStr, string? crossPhaseReason)
        {
            var task = await _taskRepository.GetTaskByIdAsync(taskId);
            if (task == null) return Json(new { success = false, message = "找不到該任務。" });

            var project = await _projectRepository.GetProjectByIdAsync(task.ProjectId);
            if (project == null) return Json(new { success = false, message = "找不到該專案。" });

            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(task.PhaseId);
            if (phase == null) return Json(new { success = false, message = "找不到該階段。" });

            // 1. Date range validations
            if (plannedStartDate > dueDate)
            {
                return Json(new { success = false, message = "計畫開始日不得晚於截止日！" });
            }

            if (plannedStartDate < project.StartDate || dueDate > project.EndDate)
            {
                return Json(new { success = false, message = $"計畫時間必須在專案起訖範圍內 ({project.StartDate:yyyy-MM-dd} ~ {project.EndDate:yyyy-MM-dd})！" });
            }

            // 2. Cross phase warning validation
            bool isCrossPhase = plannedStartDate < phase.StartDate || dueDate > phase.EndDate;
            if (isCrossPhase && string.IsNullOrWhiteSpace(crossPhaseReason))
            {
                return Json(new { 
                    success = false, 
                    requireReason = true, 
                    message = $"該日期超出所屬階段起訖 ({phase.StartDate:yyyy-MM-dd} ~ {phase.EndDate:yyyy-MM-dd})，請填寫跨階段原因後再行儲存。" 
                });
            }

            try
            {
                // Update model properties
                var oldTask = await _taskRepository.GetTaskByIdAsync(taskId);
                if (oldTask == null) return Json(new { success = false, message = "找不到該任務。" });

                task.PlannedStartDate = plannedStartDate;
                task.DueDate = dueDate;
                task.RowVersion = Convert.FromBase64String(rowVersionStr);
                task.UpdatedBy = CurrentUserId;

                var success = await _taskRepository.UpdateTaskAsync(task);
                if (success)
                {
                    var updatedTask = await _taskRepository.GetTaskByIdAsync(taskId);
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "UpdateTaskDatesGantt",
                        "MPMS_TASK",
                        taskId.ToString(),
                        oldTask,
                        new { Task = updatedTask, CrossPhaseReason = crossPhaseReason ?? "Gantt Drag adjustment" }
                    );

                    await _hubContext.Clients.All.SendAsync("TaskUpdated", taskId);

                    return Json(new { success = true, newRowVersion = Convert.ToBase64String(updatedTask!.RowVersion!) });
                }

                return Json(new { success = false, message = "日期更新失敗。" });
            }
            catch (System.Data.DBConcurrencyException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"更新日期發生錯誤：{ex.Message}" });
            }
        }
        // POST: Gantt/UpdateProjectPm
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProjectPm(int projectId, int newPmUserId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null) return Json(new { success = false, message = "找不到該專案。" });

            var oldPmUserId = project.PmUserId;
            var oldPmUserName = project.PmUserName;

            if (oldPmUserId == newPmUserId)
                return Json(new { success = false, message = "新 PM 與現有 PM 相同，無需更新。" });

            var success = await _projectRepository.UpdateProjectPmAsync(projectId, newPmUserId);
            if (!success)
                return Json(new { success = false, message = "更新 PM 失敗，請稍後再試。" });

            // Load new PM info for audit
            var updatedProject = await _projectRepository.GetProjectByIdAsync(projectId);

            await _auditService.LogAsync(
                CurrentUserId,
                "UpdateProjectPM",
                "MPMS_PROJECT",
                projectId.ToString(),
                new { PmUserId = oldPmUserId, PmUserName = oldPmUserName },
                new { PmUserId = newPmUserId, PmUserName = updatedProject?.PmUserName }
            );

            await _hubContext.Clients.All.SendAsync("ProjectUpdated", projectId);

            return Json(new { success = true, newPmUserName = updatedProject?.PmUserName });
        }

        // GET: Gantt/GetPmCandidates
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> GetPmCandidates()
        {
            var candidates = await _projectRepository.GetPmCandidatesAsync();
            return Json(candidates.Select(u => new { u.UserId, u.UserName }));
        }
    }
}
