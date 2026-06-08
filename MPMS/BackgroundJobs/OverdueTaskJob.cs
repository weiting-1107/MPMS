using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using Microsoft.Data.SqlClient;
using MPMS.Services;

namespace MPMS.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class OverdueTaskJob : IJob
    {
        private readonly string _connectionString;
        private readonly NotificationService _notificationService;
        private readonly ILogger<OverdueTaskJob> _logger;

        public OverdueTaskJob(
            IConfiguration configuration,
            NotificationService notificationService,
            ILogger<OverdueTaskJob> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is missing.");
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("OverdueTaskJob running...");

            using var conn = new SqlConnection(_connectionString);
            
            // Query all active tasks that are past their due date and not completed (Done)
            const string sqlOverdue = @"
                SELECT t.task_id as TaskId, t.task_title as TaskTitle, t.owner_user_id as OwnerUserId, t.due_date as DueDate
                FROM dbo.MPMS_TASK t
                WHERE t.task_status <> 'Done' AND t.due_date < CAST(GETDATE() as date)";

            var overdueTasks = await conn.QueryAsync(sqlOverdue);

            foreach (var task in overdueTasks)
            {
                int taskId = task.TaskId;
                string taskTitle = task.TaskTitle;
                int ownerUserId = task.OwnerUserId;
                DateTime dueDate = task.DueDate;

                // Check if we have already sent an OverdueReminder notification for this task TODAY
                const string sqlCheckSentToday = @"
                    SELECT COUNT(*) 
                    FROM dbo.MPMS_NOTIFICATION 
                    WHERE receiver_user_id = @UserId 
                      AND event_type = 'OverdueReminder' 
                      AND ref_type = 'TASK' 
                      AND ref_id = @TaskId 
                      AND created_at >= CAST(GETDATE() as date)";

                var count = await conn.ExecuteScalarAsync<int>(sqlCheckSentToday, new { UserId = ownerUserId, TaskId = taskId });

                if (count == 0)
                {
                    _logger.LogInformation($"[OVERDUE REMINDER] Task '{taskTitle}' (ID: {taskId}) is overdue (Due: {dueDate:yyyy-MM-dd}). Sending notification to user ID: {ownerUserId}...");
                    
                    var title = "任務逾期提醒 🚧";
                    var message = $"您的任務「{taskTitle}」截止日期為 {dueDate:yyyy-MM-dd}，目前已逾期。請儘速處理，並上傳佐證附件送審，或回報卡關原因。";

                    await _notificationService.SendNotificationAsync(
                        receiverUserId: ownerUserId,
                        eventType: "OverdueReminder",
                        title: title,
                        message: message,
                        refType: "TASK",
                        refId: taskId
                    );
                }
                else
                {
                    _logger.LogInformation($"[OVERDUE REMINDER] Overdue notification already sent today for Task ID: {taskId} to User ID: {ownerUserId}. Skipping.");
                }
            }
        }
    }
}
