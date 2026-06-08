using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPMS.Repositories;

namespace MPMS.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly TaskRepository _taskRepository;

        public DashboardController(TaskRepository taskRepository)
        {
            _taskRepository = taskRepository;
        }

        private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        public async Task<IActionResult> Index()
        {
            var myTasks = await _taskRepository.GetTasksByOwnerAsync(CurrentUserId);
            var assistTasks = await _taskRepository.GetTasksByAssistAsync(CurrentUserId);
            var reviewTasks = await _taskRepository.GetTasksForReviewAsync(CurrentUserId);

            ViewBag.MyTasks = myTasks;
            ViewBag.AssistTasks = assistTasks;
            ViewBag.ReviewTasks = reviewTasks;

            return View();
        }
    }
}
