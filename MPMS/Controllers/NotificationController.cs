using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPMS.Services;

namespace MPMS.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly NotificationService _notificationService;

        public NotificationController(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // GET: Notification/Index (收件匣)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var notifications = await _notificationService.GetUserNotificationsAsync(CurrentUserId);
            return View(notifications);
        }

        // POST: Notification/MarkAsRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var success = await _notificationService.MarkAsReadAsync(id, CurrentUserId);
            if (success)
            {
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "無法標記為已讀。" });
        }

        // POST: Notification/MarkAllAsRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var success = await _notificationService.MarkAllAsReadAsync(CurrentUserId);
            if (success)
            {
                TempData["SuccessMessage"] = "已將所有通知標記為已讀。";
            }
            else
            {
                TempData["ErrorMessage"] = "標記已讀操作失敗。";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Notification/GetUnreadCount
        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var count = await _notificationService.GetUnreadCountAsync(CurrentUserId);
            return Json(new { count = count });
        }
    }
}
