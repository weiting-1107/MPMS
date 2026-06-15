using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MPMS.Models;
using MPMS.Repositories;
using MPMS.Services;

namespace MPMS.Controllers
{
    [Authorize]
    public class ProjectController : Controller
    {
        private readonly ProjectRepository _projectRepository;
        private readonly ProjectPhaseRepository _projectPhaseRepository;
        private readonly TaskRepository _taskRepository;
        private readonly AuditService _auditService;

        public ProjectController(
            ProjectRepository projectRepository,
            ProjectPhaseRepository projectPhaseRepository,
            TaskRepository taskRepository,
            AuditService auditService)
        {
            _projectRepository = projectRepository;
            _projectPhaseRepository = projectPhaseRepository;
            _taskRepository = taskRepository;
            _auditService = auditService;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // PRJ-003: 全局專案看板 (所有角色皆可查看)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var projects = await _projectRepository.GetProjectsAsync();
            var phases = await _projectPhaseRepository.GetPhasesAsync();

            ViewBag.Phases = phases;
            return View(projects);
        }

        // PRJ-001: 專案管理列表 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Manage()
        {
            var projects = await _projectRepository.GetProjectsAsync();
            return View(projects);
        }

        // PRJ-001: 新增專案 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Create()
        {
            var pmCandidates = await _projectRepository.GetPmCandidatesAsync();
            ViewBag.PmCandidates = new SelectList(pmCandidates, "UserId", "UserName");
            
            return View(new Project 
            { 
                StartDate = DateTime.Today, 
                EndDate = DateTime.Today.AddMonths(3),
                IsActive = true,
                ProjectStatus = "Planning"
            });
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Project model)
        {
            // Validation: Start Date must be <= End Date
            if (model.StartDate > model.EndDate)
            {
                ModelState.AddModelError(string.Empty, "專案開始日期必須小於或等於結束日期。");
            }

            // Check duplicate project code
            var existingProject = await _projectRepository.GetProjectByCodeAsync(model.ProjectCode);
            if (existingProject != null)
            {
                ModelState.AddModelError(nameof(model.ProjectCode), "此專案代號已存在，請使用不同的專案代號。");
            }

            if (ModelState.IsValid)
            {
                var success = await _projectRepository.CreateProjectAsync(model);
                if (success)
                {
                    var createdProj = await _projectRepository.GetProjectByCodeAsync(model.ProjectCode);
                    if (createdProj != null)
                    {
                        await _auditService.LogAsync(
                            CurrentUserId, 
                            "CreateProject", 
                            "MPMS_PROJECT", 
                            createdProj.ProjectId.ToString(), 
                            null, 
                            createdProj
                        );
                    }
                    return RedirectToAction(nameof(Manage));
                }
                ModelState.AddModelError(string.Empty, "建立專案失敗，請聯絡管理員。");
            }

            var pmCandidates = await _projectRepository.GetPmCandidatesAsync();
            ViewBag.PmCandidates = new SelectList(pmCandidates, "UserId", "UserName", model.PmUserId);
            return View(model);
        }

        // PRJ-001: 編輯專案 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Edit(int id)
        {
            var project = await _projectRepository.GetProjectByIdAsync(id);
            if (project == null)
            {
                return NotFound();
            }

            var pmCandidates = await _projectRepository.GetPmCandidatesAsync();
            ViewBag.PmCandidates = new SelectList(pmCandidates, "UserId", "UserName", project.PmUserId);
            return View(project);
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Project model)
        {
            if (model.StartDate > model.EndDate)
            {
                ModelState.AddModelError(string.Empty, "專案開始日期必須小於或等於結束日期。");
            }

            if (ModelState.IsValid)
            {
                var oldProject = await _projectRepository.GetProjectByIdAsync(model.ProjectId);
                if (oldProject == null)
                {
                    return NotFound();
                }

                var success = await _projectRepository.UpdateProjectAsync(model);
                if (success)
                {
                    var updatedProject = await _projectRepository.GetProjectByIdAsync(model.ProjectId);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "UpdateProject", 
                        "MPMS_PROJECT", 
                        model.ProjectId.ToString(), 
                        oldProject, 
                        updatedProject
                    );
                    return RedirectToAction(nameof(Manage));
                }
                ModelState.AddModelError(string.Empty, "更新專案失敗。");
            }

            var pmCandidates = await _projectRepository.GetPmCandidatesAsync();
            ViewBag.PmCandidates = new SelectList(pmCandidates, "UserId", "UserName", model.PmUserId);
            return View(model);
        }

        // PRJ-002: 專案進度手動調整 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> AdjustProgress(int id)
        {
            var project = await _projectRepository.GetProjectByIdAsync(id);
            if (project == null)
            {
                return NotFound();
            }
            return View(project);
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustProgress(int projectId, decimal manualProgressPct, string? progressNote)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null)
            {
                return NotFound();
            }

            if (manualProgressPct < 0 || manualProgressPct > 100)
            {
                ModelState.AddModelError(string.Empty, "進度百分比必須介於 0% 到 100% 之間。");
            }

            if (ModelState.IsValid)
            {
                var oldProject = await _projectRepository.GetProjectByIdAsync(projectId);
                var success = await _projectRepository.UpdateProjectProgressAsync(projectId, manualProgressPct, progressNote);
                if (success)
                {
                    var updatedProject = await _projectRepository.GetProjectByIdAsync(projectId);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "AdjustProjectProgress", 
                        "MPMS_PROJECT", 
                        projectId.ToString(), 
                        oldProject, 
                        updatedProject
                    );
                    return RedirectToAction(nameof(Manage));
                }
                ModelState.AddModelError(string.Empty, "調整專案進度失敗。");
            }

            return View(project);
        }

        // PHS-001: 專案階段維護列表 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> Phases(int projectId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null)
            {
                return NotFound();
            }

            var phases = await _projectPhaseRepository.GetPhasesByProjectIdAsync(projectId);
            var tasks = await _taskRepository.GetTasksByProjectIdAsync(projectId);
            ViewBag.Project = project;
            ViewBag.Tasks = tasks;
            return View(phases);
        }

        // PHS-001: 新增專案階段 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> CreatePhase(int projectId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null)
            {
                return NotFound();
            }

            ViewBag.Project = project;
            return View(new ProjectPhase 
            { 
                ProjectId = projectId, 
                StartDate = project.StartDate,
                EndDate = project.StartDate.AddDays(30) < project.EndDate ? project.StartDate.AddDays(30) : project.EndDate,
                SortNo = 10,
                PhaseStatus = "Planned"
            });
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePhase(ProjectPhase model)
        {
            var project = await _projectRepository.GetProjectByIdAsync(model.ProjectId);
            if (project == null)
            {
                return NotFound();
            }

            // Validation: Dates
            if (model.StartDate > model.EndDate)
            {
                ModelState.AddModelError(string.Empty, "階段開始日期不得晚於結束日期。");
            }
            if (model.StartDate < project.StartDate || model.EndDate > project.EndDate)
            {
                ModelState.AddModelError(string.Empty, $"階段起訖日必須落在專案的起訖日期內 ({project.StartDate:yyyy-MM-dd} ~ {project.EndDate:yyyy-MM-dd})。");
            }

            if (ModelState.IsValid)
            {
                var success = await _projectPhaseRepository.CreatePhaseAsync(model);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "CreatePhase", 
                        "MPMS_PROJECT_PHASE", 
                        model.PhaseName, 
                        null, 
                        model
                    );
                    return RedirectToAction(nameof(Phases), new { projectId = model.ProjectId });
                }
                ModelState.AddModelError(string.Empty, "建立專案階段失敗。");
            }

            ViewBag.Project = project;
            return View(model);
        }

        // PHS-001: 編輯專案階段 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> EditPhase(int id)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(id);
            if (phase == null)
            {
                return NotFound();
            }

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            ViewBag.Project = project;
            return View(phase);
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPhase(ProjectPhase model)
        {
            var project = await _projectRepository.GetProjectByIdAsync(model.ProjectId);
            if (project == null)
            {
                return NotFound();
            }

            // Validation: Dates
            if (model.StartDate > model.EndDate)
            {
                ModelState.AddModelError(string.Empty, "階段開始日期不得晚於結束日期。");
            }
            if (model.StartDate < project.StartDate || model.EndDate > project.EndDate)
            {
                ModelState.AddModelError(string.Empty, $"階段起訖日必須落在專案的起訖日期內 ({project.StartDate:yyyy-MM-dd} ~ {project.EndDate:yyyy-MM-dd})。");
            }

            if (ModelState.IsValid)
            {
                var oldPhase = await _projectPhaseRepository.GetPhaseByIdAsync(model.PhaseId);
                var success = await _projectPhaseRepository.UpdatePhaseAsync(model);
                if (success)
                {
                    var updatedPhase = await _projectPhaseRepository.GetPhaseByIdAsync(model.PhaseId);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "UpdatePhase", 
                        "MPMS_PROJECT_PHASE", 
                        model.PhaseId.ToString(), 
                        oldPhase, 
                        updatedPhase
                    );
                    return RedirectToAction(nameof(Phases), new { projectId = model.ProjectId });
                }
                ModelState.AddModelError(string.Empty, "更新專案階段失敗。");
            }

            ViewBag.Project = project;
            return View(model);
        }

        // PHS-002: 專案階段進度手動調整 (僅限 Admin / PM)
        [HttpGet]
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> AdjustPhaseProgress(int id)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(id);
            if (phase == null)
            {
                return NotFound();
            }

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            ViewBag.Project = project;
            return View(phase);
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustPhaseProgress(int phaseId, decimal manualProgressPct, string phaseStatus)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(phaseId);
            if (phase == null)
            {
                return NotFound();
            }

            if (manualProgressPct < 0 || manualProgressPct > 100)
            {
                ModelState.AddModelError(string.Empty, "進度百分比必須介於 0% 到 100% 之間。");
            }

            if (ModelState.IsValid)
            {
                var oldPhase = await _projectPhaseRepository.GetPhaseByIdAsync(phaseId);
                var success = await _projectPhaseRepository.UpdatePhaseProgressAsync(phaseId, manualProgressPct, phaseStatus);
                if (success)
                {
                    var updatedPhase = await _projectPhaseRepository.GetPhaseByIdAsync(phaseId);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "AdjustPhaseProgress", 
                        "MPMS_PROJECT_PHASE", 
                        phaseId.ToString(), 
                        oldPhase, 
                        updatedPhase
                    );
                    return RedirectToAction(nameof(Phases), new { projectId = phase.ProjectId });
                }
                ModelState.AddModelError(string.Empty, "調整專案階段進度失敗。");
            }

            var project = await _projectRepository.GetProjectByIdAsync(phase.ProjectId);
            ViewBag.Project = project;
            return View(phase);
        }

        // DELETE: Project/DeleteProject (僅限 Admin / PM)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProject(int projectId)
        {
            var project = await _projectRepository.GetProjectByIdAsync(projectId);
            if (project == null)
            {
                TempData["ErrorMessage"] = "找不到該專案。";
                return RedirectToAction(nameof(Manage));
            }

            try
            {
                var success = await _projectRepository.DeleteProjectAsync(projectId);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "DeleteProject",
                        "MPMS_PROJECT",
                        projectId.ToString(),
                        project,
                        null
                    );
                    TempData["SuccessMessage"] = $"專案「{project.ProjectName}」及其所有階段與任務已成功刪除。";
                }
                else
                {
                    TempData["ErrorMessage"] = "刪除專案失敗，請稍後再試。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "刪除專案失敗");
            }

            return RedirectToAction(nameof(Manage));
        }

        // DELETE: Project/DeletePhase (僅限 Admin / PM)
        [HttpPost]
        [Authorize(Roles = "ADMIN,PM")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePhase(int phaseId)
        {
            var phase = await _projectPhaseRepository.GetPhaseByIdAsync(phaseId);
            if (phase == null)
            {
                TempData["ErrorMessage"] = "找不到該專案階段。";
                return RedirectToAction(nameof(Manage));
            }

            int projectId = phase.ProjectId;

            try
            {
                var success = await _projectPhaseRepository.DeletePhaseAsync(phaseId);
                if (success)
                {
                    await _auditService.LogAsync(
                        CurrentUserId,
                        "DeletePhase",
                        "MPMS_PROJECT_PHASE",
                        phaseId.ToString(),
                        phase,
                        null
                    );
                    TempData["SuccessMessage"] = $"階段「{phase.PhaseName}」及其所有任務已成功刪除。";
                }
                else
                {
                    TempData["ErrorMessage"] = "刪除階段失敗，請稍後再試。";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = MPMS.Helpers.DbErrorTranslationHelper.TranslateException(ex, "刪除階段失敗");
            }

            return RedirectToAction(nameof(Phases), new { projectId });
        }
    }
}
