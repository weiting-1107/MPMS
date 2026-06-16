using System;
using System.Data;
using System.Linq;
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
    public class BlockReminderJob : IJob
    {
        private readonly string _connectionString;
        private readonly NotificationService _notificationService;
        private readonly ILogger<BlockReminderJob> _logger;

        public BlockReminderJob(
            IConfiguration configuration,
            NotificationService notificationService,
            ILogger<BlockReminderJob> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is missing.");
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("BlockReminderJob running...");

            var now = DateTime.Now;
            var currentTime = now.TimeOfDay;

            // 1. 每日 08:45：針對預計解除日將到期或已逾期的卡關紀錄產生 BlockDueSoon 提醒。
            if (currentTime >= new TimeSpan(8, 45, 0))
            {
                await ProcessBlockDueSoonRemindersAsync();
            }

            // 2. 每日 09:00：彙整逾期未解除的卡關項目，通知對應的專案 PM。
            if (currentTime >= new TimeSpan(9, 0, 0))
            {
                await ProcessPmOverdueSummariesAsync();
            }
        }

        private async Task ProcessBlockDueSoonRemindersAsync()
        {
            _logger.LogInformation("Processing BlockDueSoon reminders...");

            using var conn = new SqlConnection(_connectionString);

            // 查詢所有活躍卡關中，預計解除日在今天或以前的紀錄
            const string sqlActiveBlocks = @"
                SELECT tbl.block_id as BlockId, tbl.task_id as TaskId, tbl.expected_resolve_date as ExpectedResolveDate,
                       t.task_title as TaskTitle, t.owner_user_id as OwnerUserId, tbl.assigned_helper_user_id as AssignedHelperUserId,
                       u_helper.user_name as HelperName, u_owner.user_name as OwnerName
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_USER u_owner ON t.owner_user_id = u_owner.user_id
                LEFT JOIN dbo.MPMS_USER u_helper ON tbl.assigned_helper_user_id = u_helper.user_id
                WHERE tbl.block_status IN ('Open', 'InProgress', 'WaitingExternal')
                  AND tbl.expected_resolve_date IS NOT NULL
                  AND tbl.expected_resolve_date <= CAST(GETDATE() as date)";

            var activeBlocks = await conn.QueryAsync(sqlActiveBlocks);

            foreach (var block in activeBlocks)
            {
                int blockId = block.BlockId;
                int taskId = block.TaskId;
                DateTime expectedResolveDate = block.ExpectedResolveDate;
                string taskTitle = block.TaskTitle;
                int ownerUserId = block.OwnerUserId;
                int? helperUserId = block.AssignedHelperUserId;
                string helperName = block.HelperName ?? "未指派協助人";
                bool isOverdue = expectedResolveDate.Date < DateTime.Today;

                // 協助人提醒
                if (helperUserId.HasValue)
                {
                    bool helperNotified = await CheckNotificationSentTodayAsync(conn, helperUserId.Value, "BlockDueSoon", taskId);
                    if (!helperNotified)
                    {
                        _logger.LogInformation($"[BLOCK REMINDER] Sending BlockDueSoon to helper ID: {helperUserId} for Task: {taskTitle} (BlockId: {blockId})");
                        await _notificationService.SendBlockDueSoonNotificationAsync(
                            receiverUserId: helperUserId.Value,
                            taskTitle: taskTitle,
                            helperName: helperName,
                            expectedResolveDate: expectedResolveDate,
                            taskId: taskId,
                            isOverdue: isOverdue
                        );
                    }
                }

                // 負責人提醒
                bool ownerNotified = await CheckNotificationSentTodayAsync(conn, ownerUserId, "BlockDueSoon", taskId);
                if (!ownerNotified)
                {
                    _logger.LogInformation($"[BLOCK REMINDER] Sending BlockDueSoon to owner ID: {ownerUserId} for Task: {taskTitle} (BlockId: {blockId})");
                    await _notificationService.SendBlockDueSoonNotificationAsync(
                        receiverUserId: ownerUserId,
                        taskTitle: taskTitle,
                        helperName: helperName,
                        expectedResolveDate: expectedResolveDate,
                        taskId: taskId,
                        isOverdue: isOverdue
                    );
                }
            }
        }

        private async Task ProcessPmOverdueSummariesAsync()
        {
            _logger.LogInformation("Processing PM overdue summaries...");

            using var conn = new SqlConnection(_connectionString);

            // 查詢所有逾期的活躍卡關紀錄，含專案 PM 資訊
            const string sqlOverdueBlocks = @"
                SELECT tbl.block_id as BlockId, tbl.task_id as TaskId, tbl.expected_resolve_date as ExpectedResolveDate,
                       t.task_title as TaskTitle, p.project_id as ProjectId, p.project_name as ProjectName, p.pm_user_id as PmUserId
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                WHERE tbl.block_status IN ('Open', 'InProgress', 'WaitingExternal')
                  AND tbl.expected_resolve_date IS NOT NULL
                  AND tbl.expected_resolve_date < CAST(GETDATE() as date)";

            var overdueBlocks = await conn.QueryAsync(sqlOverdueBlocks);

            // 按專案分組
            var groupedByProject = overdueBlocks.GroupBy(b => new { ProjectId = (int)b.ProjectId, ProjectName = (string)b.ProjectName, PmUserId = (int)b.PmUserId });

            foreach (var group in groupedByProject)
            {
                var pmUserId = group.Key.PmUserId;
                var projectId = group.Key.ProjectId;
                var projectName = group.Key.ProjectName;

                // 檢查今天是否已向該 PM 發送過該專案的彙整
                bool pmNotified = await CheckNotificationSentTodayAsync(conn, pmUserId, "BlockPmSummary", projectId, refType: "PROJECT");
                if (!pmNotified)
                {
                    _logger.LogInformation($"[BLOCK PM SUMMARY] Compiling summary for PM ID: {pmUserId} in Project: {projectName}");

                    var message = $"您好：\n\n您負責的專案【{projectName}】目前有以下逾期未排除的卡關任務，請協助協調排除：\n\n";
                    int idx = 1;
                    foreach (var item in group)
                    {
                        DateTime expDate = item.ExpectedResolveDate;
                        message += $"{idx}. 任務「{item.TaskTitle}」- 預計排除日期：{expDate:yyyy-MM-dd} (已逾期)\n";
                        idx++;
                    }
                    message += "\n請至「PM 卡關處理中心」或「週會控制台」進行指派與排除策略追蹤。";

                    await _notificationService.SendNotificationAsync(
                        receiverUserId: pmUserId,
                        eventType: "BlockPmSummary",
                        title: $"卡關項目逾期彙整報告 - {projectName} 📋",
                        message: message,
                        refType: "PROJECT",
                        refId: projectId,
                        sendEmail: true
                    );
                }
            }
        }

        private async Task<bool> CheckNotificationSentTodayAsync(IDbConnection conn, int userId, string eventType, int refId, string refType = "TASK")
        {
            const string sql = @"
                SELECT COUNT(*) 
                FROM dbo.MPMS_NOTIFICATION 
                WHERE receiver_user_id = @UserId 
                  AND event_type = @EventType 
                  AND ref_type = @RefType 
                  AND ref_id = @RefId 
                  AND created_at >= CAST(GETDATE() as date)";

            var count = await conn.ExecuteScalarAsync<int>(sql, new
            {
                UserId = userId,
                EventType = eventType,
                RefType = refType,
                RefId = refId
            });

            return count > 0;
        }
    }
}
