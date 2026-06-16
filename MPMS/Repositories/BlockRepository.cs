using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class BlockRepository : BaseRepository
    {
        public BlockRepository(IConfiguration configuration) : base(configuration)
        {
        }

        /// <summary>
        /// 建立一筆卡關紀錄
        /// </summary>
        public async Task<int> CreateBlockLogAsync(TaskBlockLog log, IDbConnection? connection = null, IDbTransaction? transaction = null)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_TASK_BLOCK_LOG (
                    task_id, block_type, block_reason, help_needed, impact_level, 
                    blocked_by, blocked_at_utc, assigned_helper_user_id, expected_resolve_date, 
                    block_status, resolve_summary, resolved_by, resolved_at_utc, 
                    is_weekly_focus, created_at_utc, updated_at_utc
                )
                VALUES (
                    @TaskId, @BlockType, @BlockReason, @HelpNeeded, @ImpactLevel, 
                    @BlockedBy, @BlockedAtUtc, @AssignedHelperUserId, @ExpectedResolveDate, 
                    @BlockStatus, @ResolveSummary, @ResolvedBy, @ResolvedAtUtc, 
                    @IsWeeklyFocus, @CreatedAtUtc, @UpdatedAtUtc
                );
                SELECT CAST(SCOPE_IDENTITY() as int);";

            if (connection != null)
            {
                return await connection.QuerySingleAsync<int>(sql, log, transaction);
            }

            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>(sql, log);
        }

        /// <summary>
        /// 更新卡關紀錄
        /// </summary>
        public async Task<bool> UpdateBlockLogAsync(TaskBlockLog log, IDbConnection? connection = null, IDbTransaction? transaction = null)
        {
            const string sql = @"
                UPDATE dbo.MPMS_TASK_BLOCK_LOG
                SET block_type = @BlockType,
                    block_reason = @BlockReason,
                    help_needed = @HelpNeeded,
                    impact_level = @ImpactLevel,
                    assigned_helper_user_id = @AssignedHelperUserId,
                    expected_resolve_date = @ExpectedResolveDate,
                    block_status = @BlockStatus,
                    resolve_summary = @ResolveSummary,
                    resolved_by = @ResolvedBy,
                    resolved_at_utc = @ResolvedAtUtc,
                    is_weekly_focus = @IsWeeklyFocus,
                    updated_at_utc = @UpdatedAtUtc
                WHERE block_id = @BlockId";

            if (connection != null)
            {
                var rows = await connection.ExecuteAsync(sql, log, transaction);
                return rows > 0;
            }

            using var conn = CreateConnection();
            var resultRows = await conn.ExecuteAsync(sql, log);
            return resultRows > 0;
        }

        /// <summary>
        /// 獲取特定任務目前活躍的卡關紀錄
        /// </summary>
        public async Task<TaskBlockLog?> GetActiveBlockLogByTaskIdAsync(int taskId, IDbConnection? connection = null, IDbTransaction? transaction = null)
        {
            const string sql = @"
                SELECT tbl.*, t.task_title as TaskTitle, t.task_status as TaskStatus,
                       u.user_name as BlockedByName
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_USER u ON tbl.blocked_by = u.user_id
                WHERE tbl.task_id = @TaskId AND tbl.block_status IN ('Open', 'InProgress', 'WaitingExternal')";

            if (connection != null)
            {
                return await connection.QueryFirstOrDefaultAsync<TaskBlockLog>(sql, new { TaskId = taskId }, transaction);
            }

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<TaskBlockLog>(sql, new { TaskId = taskId });
        }

        /// <summary>
        /// 獲取特定卡關紀錄詳情
        /// </summary>
        public async Task<TaskBlockLog?> GetBlockLogByIdAsync(int blockId)
        {
            const string sql = @"
                SELECT tbl.*, 
                       t.task_title as TaskTitle, t.due_date as DueDate, t.is_milestone as IsMilestone, t.owner_user_id as OwnerUserId, t.task_status as TaskStatus,
                       u_block.user_name as BlockedByName,
                       u_helper.user_name as HelperName,
                       u_resolved.user_name as ResolvedByName,
                       p.project_name as ProjectName, p.project_id as ProjectId,
                       ph.phase_name as PhaseName,
                       u_owner.user_name as OwnerUserName
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_USER u_block ON tbl.blocked_by = u_block.user_id
                LEFT JOIN dbo.MPMS_USER u_helper ON tbl.assigned_helper_user_id = u_helper.user_id
                LEFT JOIN dbo.MPMS_USER u_resolved ON tbl.resolved_by = u_resolved.user_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u_owner ON t.owner_user_id = u_owner.user_id
                WHERE tbl.block_id = @BlockId";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<TaskBlockLog>(sql, new { BlockId = blockId });
        }

        /// <summary>
        /// 獲取卡關列表 (PM 卡關處理中心與報表)
        /// </summary>
        public async Task<IEnumerable<TaskBlockLog>> GetBlockLogsAsync(
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
            var sql = @"
                SELECT tbl.*, 
                       t.task_title as TaskTitle, t.due_date as DueDate, t.is_milestone as IsMilestone, t.owner_user_id as OwnerUserId, t.task_status as TaskStatus,
                       u_block.user_name as BlockedByName,
                       u_helper.user_name as HelperName,
                       u_resolved.user_name as ResolvedByName,
                       p.project_name as ProjectName, p.project_id as ProjectId,
                       ph.phase_name as PhaseName,
                       u_owner.user_name as OwnerUserName
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_USER u_block ON tbl.blocked_by = u_block.user_id
                LEFT JOIN dbo.MPMS_USER u_helper ON tbl.assigned_helper_user_id = u_helper.user_id
                LEFT JOIN dbo.MPMS_USER u_resolved ON tbl.resolved_by = u_resolved.user_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u_owner ON t.owner_user_id = u_owner.user_id
                WHERE 1 = 1";

            var parameters = new DynamicParameters();

            if (projectId.HasValue)
            {
                sql += " AND ph.project_id = @ProjectId";
                parameters.Add("ProjectId", projectId.Value);
            }

            if (phaseId.HasValue)
            {
                sql += " AND t.phase_id = @PhaseId";
                parameters.Add("PhaseId", phaseId.Value);
            }

            if (ownerUserId.HasValue)
            {
                sql += " AND t.owner_user_id = @OwnerUserId";
                parameters.Add("OwnerUserId", ownerUserId.Value);
            }

            if (!string.IsNullOrEmpty(blockType))
            {
                sql += " AND tbl.block_type = @BlockType";
                parameters.Add("BlockType", blockType);
            }

            if (!string.IsNullOrEmpty(impactLevel))
            {
                sql += " AND tbl.impact_level = @ImpactLevel";
                parameters.Add("ImpactLevel", impactLevel);
            }

            if (isMilestone.HasValue)
            {
                sql += " AND t.is_milestone = @IsMilestone";
                parameters.Add("IsMilestone", isMilestone.Value ? 1 : 0);
            }

            if (!string.IsNullOrEmpty(blockStatus))
            {
                sql += " AND tbl.block_status = @BlockStatus";
                parameters.Add("BlockStatus", blockStatus);
            }

            if (isWeeklyFocus.HasValue)
            {
                sql += " AND tbl.is_weekly_focus = @IsWeeklyFocus";
                parameters.Add("IsWeeklyFocus", isWeeklyFocus.Value ? 1 : 0);
            }

            if (isOverdue.HasValue)
            {
                if (isOverdue.Value)
                {
                    sql += " AND tbl.block_status IN ('Open', 'InProgress', 'WaitingExternal') AND tbl.expected_resolve_date < CAST(GETDATE() AS DATE)";
                }
                else
                {
                    sql += " AND (tbl.expected_resolve_date >= CAST(GETDATE() AS DATE) OR tbl.expected_resolve_date IS NULL OR tbl.block_status IN ('Resolved', 'Cancelled'))";
                }
            }

            // 排序規則：里程碑優先、逾期優先、卡關天數長優先 (即申報時間愈早優先)、高影響優先、最後更新時間倒序
            sql += @"
                ORDER BY 
                    t.is_milestone DESC,
                    CASE 
                        WHEN tbl.block_status IN ('Open', 'InProgress', 'WaitingExternal') AND tbl.expected_resolve_date < CAST(GETDATE() AS DATE) THEN 1 
                        ELSE 0 
                    END DESC,
                    tbl.blocked_at_utc ASC,
                    CASE tbl.impact_level 
                        WHEN 'High' THEN 1 
                        WHEN 'Medium' THEN 2 
                        WHEN 'Low' THEN 3 
                        ELSE 4 
                    END ASC,
                    ISNULL(tbl.updated_at_utc, tbl.created_at_utc) DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskBlockLog>(sql, parameters);
        }

        /// <summary>
        /// 獲取特定任務的所有歷史卡關紀錄 (含已解除與已取消)
        /// </summary>
        public async Task<IEnumerable<TaskBlockLog>> GetBlockLogsByTaskIdAsync(int taskId)
        {
            const string sql = @"
                SELECT tbl.*, 
                       t.task_title as TaskTitle,
                       u_block.user_name as BlockedByName,
                       u_helper.user_name as HelperName,
                       u_resolved.user_name as ResolvedByName
                FROM dbo.MPMS_TASK_BLOCK_LOG tbl
                INNER JOIN dbo.MPMS_TASK t ON tbl.task_id = t.task_id
                INNER JOIN dbo.MPMS_USER u_block ON tbl.blocked_by = u_block.user_id
                LEFT JOIN dbo.MPMS_USER u_helper ON tbl.assigned_helper_user_id = u_helper.user_id
                LEFT JOIN dbo.MPMS_USER u_resolved ON tbl.resolved_by = u_resolved.user_id
                WHERE tbl.task_id = @TaskId
                ORDER BY tbl.blocked_at_utc DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskBlockLog>(sql, new { TaskId = taskId });
        }

        /// <summary>
        /// 建立一筆卡關評論/留言
        /// </summary>
        public async Task<int> CreateBlockCommentAsync(TaskBlockComment comment, IDbConnection? connection = null, IDbTransaction? transaction = null)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_TASK_BLOCK_COMMENT (
                    block_id, comment_type, comment_text, old_block_status, new_block_status, created_by, created_at_utc
                )
                VALUES (
                    @BlockId, @CommentType, @CommentText, @OldBlockStatus, @NewBlockStatus, @CreatedBy, @CreatedAtUtc
                );
                SELECT CAST(SCOPE_IDENTITY() as int);";

            if (connection != null)
            {
                return await connection.QuerySingleAsync<int>(sql, comment, transaction);
            }

            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>(sql, comment);
        }

        /// <summary>
        /// 獲取特定卡關紀錄底下的所有留言與處理歷程
        /// </summary>
        public async Task<IEnumerable<TaskBlockComment>> GetCommentsByBlockIdAsync(int blockId)
        {
            const string sql = @"
                SELECT tbc.*, u.user_name as CreatorName
                FROM dbo.MPMS_TASK_BLOCK_COMMENT tbc
                INNER JOIN dbo.MPMS_USER u ON tbc.created_by = u.user_id
                WHERE tbc.block_id = @BlockId
                ORDER BY tbc.created_at_utc ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskBlockComment>(sql, new { BlockId = blockId });
        }

        /// <summary>
        /// 獲取待同儕審查任務清單 (附帶篩選條件)
        /// </summary>
        public async Task<IEnumerable<TaskModel>> GetReviewingTasksWithFiltersAsync(
            int? projectId = null,
            int? phaseId = null,
            int? ownerUserId = null,
            bool? isMilestone = null)
        {
            var sql = @"
                SELECT t.*, 
                       ph.phase_name as PhaseName, 
                       p.project_name as ProjectName, 
                       p.project_id as ProjectId, 
                       u_owner.user_name as OwnerUserName,
                       u_rev.user_name as ReviewerUserName,
                       r.review_id as CurrentReviewId,
                       r.row_version as ReviewRowVersion
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u_owner ON t.owner_user_id = u_owner.user_id
                LEFT JOIN dbo.MPMS_USER u_rev ON t.reviewer_user_id = u_rev.user_id
                LEFT JOIN dbo.MPMS_TASK_REVIEW r ON t.current_review_id = r.review_id AND r.review_result = 'Pending'
                WHERE t.task_status = 'Reviewing'";

            var parameters = new DynamicParameters();

            if (projectId.HasValue)
            {
                sql += " AND ph.project_id = @ProjectId";
                parameters.Add("ProjectId", projectId.Value);
            }

            if (phaseId.HasValue)
            {
                sql += " AND t.phase_id = @PhaseId";
                parameters.Add("PhaseId", phaseId.Value);
            }

            if (ownerUserId.HasValue)
            {
                sql += " AND t.owner_user_id = @OwnerUserId";
                parameters.Add("OwnerUserId", ownerUserId.Value);
            }

            if (isMilestone.HasValue)
            {
                sql += " AND t.is_milestone = @IsMilestone";
                parameters.Add("IsMilestone", isMilestone.Value ? 1 : 0);
            }

            sql += " ORDER BY t.review_requested_at DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, parameters);
        }

        /// <summary>
        /// 批次獲取多個任務的附件
        /// </summary>
        public async Task<IEnumerable<TaskAttachment>> GetAttachmentsForTasksAsync(IEnumerable<int> taskIds)
        {
            if (taskIds == null || !taskIds.Any())
            {
                return new List<TaskAttachment>();
            }

            const string sql = @"
                SELECT a.*, u.user_name as UploadUserName
                FROM dbo.MPMS_TASK_ATTACHMENT a
                INNER JOIN dbo.MPMS_USER u ON a.upload_user_id = u.user_id
                WHERE a.task_id IN @TaskIds AND a.is_void = 0";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskAttachment>(sql, new { TaskIds = taskIds });
        }
    }
}
