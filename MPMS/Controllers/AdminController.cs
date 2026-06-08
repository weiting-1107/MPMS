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
    public class AdminController : Controller
    {
        private readonly UserRepository _userRepository;
        private readonly AuditLogRepository _auditLogRepository;
        private readonly AuthService _authService;
        private readonly AuditService _auditService;

        public AdminController(
            UserRepository userRepository,
            AuditLogRepository auditLogRepository,
            AuthService authService,
            AuditService auditService)
        {
            _userRepository = userRepository;
            _auditLogRepository = auditLogRepository;
            _authService = authService;
            _auditService = auditService;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // SYS-002: 使用者維護 - 列表 (僅限 Admin)
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> Users()
        {
            var users = await _userRepository.GetUsersAsync();
            return View(users);
        }

        // SYS-002: 使用者維護 - 新增 (僅限 Admin)
        [HttpGet]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> CreateUser()
        {
            var roles = await _userRepository.GetRolesAsync();
            ViewBag.Roles = new SelectList(roles, "RoleId", "RoleName");
            return View(new User { IsActive = true });
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(User model, string rawPassword)
        {
            if (string.IsNullOrWhiteSpace(rawPassword))
            {
                ModelState.AddModelError(nameof(rawPassword), "密碼不可為空。");
            }

            if (ModelState.IsValid)
            {
                // Encrypt password using AuthService
                model.PasswordHash = _authService.HashPassword(rawPassword);
                
                var success = await _userRepository.CreateUserAsync(model);
                if (success)
                {
                    // Query the new user to get exact ID for logging (optional, or just log based on model account)
                    var newUser = await _userRepository.GetUserByAccountAsync(model.Account);
                    if (newUser != null)
                    {
                        await _auditService.LogAsync(
                            CurrentUserId, 
                            "CreateUser", 
                            "MPMS_USER", 
                            newUser.UserId.ToString(), 
                            null, 
                            newUser
                        );
                    }
                    return RedirectToAction(nameof(Users));
                }
                ModelState.AddModelError(string.Empty, "新增使用者失敗。");
            }

            var roles = await _userRepository.GetRolesAsync();
            ViewBag.Roles = new SelectList(roles, "RoleId", "RoleName", model.RoleId);
            return View(model);
        }

        // SYS-002: 使用者維護 - 編輯 (僅限 Admin)
        [HttpGet]
        [Authorize(Roles = "ADMIN")]
        public async Task<IActionResult> EditUser(int id)
        {
            var users = await _userRepository.GetUsersAsync();
            User? userToEdit = null;
            foreach (var u in users)
            {
                if (u.UserId == id)
                {
                    userToEdit = u;
                    break;
                }
            }

            if (userToEdit == null)
            {
                return NotFound();
            }

            var roles = await _userRepository.GetRolesAsync();
            ViewBag.Roles = new SelectList(roles, "RoleId", "RoleName", userToEdit.RoleId);
            return View(userToEdit);
        }

        [HttpPost]
        [Authorize(Roles = "ADMIN")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(User model)
        {
            if (ModelState.IsValid)
            {
                // Fetch the old user from database for audit logging comparison
                var oldUsers = await _userRepository.GetUsersAsync();
                User? oldUser = null;
                foreach (var u in oldUsers)
                {
                    if (u.UserId == model.UserId)
                    {
                        oldUser = u;
                        break;
                    }
                }

                if (oldUser == null)
                {
                    return NotFound();
                }

                var success = await _userRepository.UpdateUserAsync(model);
                if (success)
                {
                    var updatedUser = await _userRepository.GetUserByAccountAsync(oldUser.Account);
                    await _auditService.LogAsync(
                        CurrentUserId, 
                        "UpdateUser", 
                        "MPMS_USER", 
                        model.UserId.ToString(), 
                        oldUser, 
                        updatedUser
                    );
                    return RedirectToAction(nameof(Users));
                }
                ModelState.AddModelError(string.Empty, "更新使用者失敗。");
            }

            var roles = await _userRepository.GetRolesAsync();
            ViewBag.Roles = new SelectList(roles, "RoleId", "RoleName", model.RoleId);
            return View(model);
        }

        // SYS-004: 系統稽核日誌查詢 (Admin / PM)
        [Authorize(Roles = "ADMIN,PM")]
        public async Task<IActionResult> AuditLogs()
        {
            var logs = await _auditLogRepository.GetAuditLogsAsync();
            return View(logs);
        }
    }
}
