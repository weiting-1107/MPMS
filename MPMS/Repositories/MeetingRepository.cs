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
    public class MeetingRepository : BaseRepository
    {
        public MeetingRepository(IConfiguration configuration) : base(configuration)
        {
        }

        // Get all meetings
        public async Task<IEnumerable<Meeting>> GetMeetingsAsync()
        {
            const string sql = @"
                SELECT m.*, p.project_name as ProjectName, u.user_name as HostUserName
                FROM dbo.MPMS_MEETING m
                LEFT JOIN dbo.MPMS_PROJECT p ON m.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON m.host_user_id = u.user_id
                ORDER BY m.meeting_date DESC, m.meeting_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<Meeting>(sql);
        }

        // Get meeting by ID
        public async Task<Meeting?> GetMeetingByIdAsync(int meetingId)
        {
            const string sql = @"
                SELECT m.*, p.project_name as ProjectName, u.user_name as HostUserName
                FROM dbo.MPMS_MEETING m
                LEFT JOIN dbo.MPMS_PROJECT p ON m.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON m.host_user_id = u.user_id
                WHERE m.meeting_id = @MeetingId";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Meeting>(sql, new { MeetingId = meetingId });
        }

        // Create meeting
        public async Task<int> CreateMeetingAsync(Meeting meeting)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_MEETING (meeting_week, meeting_date, project_id, host_user_id, meeting_note, created_at)
                VALUES (@MeetingWeek, @MeetingDate, @ProjectId, @HostUserId, @MeetingNote, GETDATE());
                SELECT CAST(SCOPE_IDENTITY() as int);";

            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>(sql, meeting);
        }

        // Update meeting note
        public async Task<bool> UpdateMeetingNoteAsync(int meetingId, string? meetingNote)
        {
            const string sql = @"
                UPDATE dbo.MPMS_MEETING
                SET meeting_note = @MeetingNote
                WHERE meeting_id = @MeetingId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { MeetingId = meetingId, MeetingNote = meetingNote });
            return rows > 0;
        }

        // Get action items for a meeting
        public async Task<IEnumerable<MeetingAction>> GetMeetingActionsAsync(int meetingId)
        {
            const string sql = @"
                SELECT a.*, u.user_name as OwnerUserName, t.task_title as AssociatedTaskTitle
                FROM dbo.MPMS_MEETING_ACTION a
                INNER JOIN dbo.MPMS_USER u ON a.owner_user_id = u.user_id
                LEFT JOIN dbo.MPMS_TASK t ON a.task_id = t.task_id
                WHERE a.meeting_id = @MeetingId
                ORDER BY a.action_id ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<MeetingAction>(sql, new { MeetingId = meetingId });
        }

        // Create action item
        public async Task<bool> CreateMeetingActionAsync(MeetingAction action)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_MEETING_ACTION (meeting_id, action_title, owner_user_id, due_date, action_note, created_at)
                VALUES (@MeetingId, @ActionTitle, @OwnerUserId, @DueDate, @ActionNote, GETDATE())";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, action);
            return rows > 0;
        }

        // Update action item task association
        public async Task<bool> UpdateActionTaskAssociationAsync(int actionId, int taskId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_MEETING_ACTION
                SET task_id = @TaskId
                WHERE action_id = @ActionId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { ActionId = actionId, TaskId = taskId });
            return rows > 0;
        }

        // ==================== Weekly Dashboard Queries ====================

        // 1. 本週截止任務 (Due This Week)
        public async Task<IEnumerable<TaskModel>> GetTasksDueThisWeekAsync(DateTime startOfWeek, DateTime endOfWeek, int? projectId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.due_date >= @StartOfWeek AND t.due_date <= @EndOfWeek
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { StartOfWeek = startOfWeek, EndOfWeek = endOfWeek, ProjectId = projectId });
        }

        // 2. 當週狀態更新任務 (Updated This Week)
        public async Task<IEnumerable<TaskModel>> GetTasksUpdatedThisWeekAsync(DateTime startOfWeek, DateTime endOfWeek, int? projectId)
        {
            const string sql = @"
                SELECT DISTINCT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                INNER JOIN dbo.MPMS_TASK_STATUS_LOG l ON t.task_id = l.task_id
                WHERE l.changed_at >= @StartOfWeek AND l.changed_at <= @EndOfWeek
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { StartOfWeek = startOfWeek, EndOfWeek = endOfWeek, ProjectId = projectId });
        }

        // 3. 目前卡關任務 (Blocked Tasks)
        public async Task<IEnumerable<TaskModel>> GetBlockedTasksAsync(int? projectId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.task_status = 'Blocked'
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { ProjectId = projectId });
        }

        // 4. 目前審查中任務 (Reviewing Tasks)
        public async Task<IEnumerable<TaskModel>> GetReviewingTasksAsync(int? projectId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.task_status = 'Reviewing'
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { ProjectId = projectId });
        }

        // 5. 本週完成任務 (Completed This Week)
        public async Task<IEnumerable<TaskModel>> GetTasksCompletedThisWeekAsync(DateTime startOfWeek, DateTime endOfWeek, int? projectId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.task_status = 'Done' AND t.done_at >= @StartOfWeek AND t.done_at <= @EndOfWeek
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY t.done_at DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { StartOfWeek = startOfWeek, EndOfWeek = endOfWeek, ProjectId = projectId });
        }

        // 6. 週會附件牆 (Attachment Wall)
        public async Task<IEnumerable<TaskAttachment>> GetAttachmentWallAsync(DateTime startOfWeek, DateTime endOfWeek, int? projectId)
        {
            const string sql = @"
                SELECT a.*, t.task_title as TaskTitle, u.user_name as UploadUserName
                FROM dbo.MPMS_TASK_ATTACHMENT a
                INNER JOIN dbo.MPMS_TASK t ON a.task_id = t.task_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON a.upload_user_id = u.user_id
                WHERE a.is_void = 0 
                  AND a.uploaded_at >= @StartOfWeek AND a.uploaded_at <= @EndOfWeek
                  AND t.task_status IN ('Reviewing', 'Done')
                  AND (@ProjectId IS NULL OR p.project_id = @ProjectId)
                ORDER BY a.uploaded_at DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskAttachment>(sql, new { StartOfWeek = startOfWeek, EndOfWeek = endOfWeek, ProjectId = projectId });
        }

        // Convert Meeting Action Item to Formal Task (Transactional)
        public async Task<int> ConvertActionToTaskAsync(int actionId, int phaseId, string priority, int operatorUserId)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Fetch action item
                const string sqlGetAction = "SELECT * FROM dbo.MPMS_MEETING_ACTION WHERE action_id = @ActionId";
                var action = await conn.QueryFirstOrDefaultAsync<MeetingAction>(sqlGetAction, new { ActionId = actionId }, transaction: trans);
                if (action == null)
                {
                    trans.Rollback();
                    return 0;
                }

                // 2. Insert into MPMS_TASK
                const string sqlTask = @"
                    INSERT INTO dbo.MPMS_TASK (
                        phase_id, task_title, task_desc, owner_user_id, due_date, 
                        task_status, priority, created_by, updated_by, created_at, 
                        updated_at, planned_start_date, is_milestone, gantt_sort_no
                    )
                    OUTPUT INSERTED.task_id
                    VALUES (
                        @PhaseId, @TaskTitle, @TaskDesc, @OwnerUserId, @DueDate, 
                        'Todo', @Priority, @OperatorUserId, @OperatorUserId, GETDATE(), 
                        GETDATE(), @PlannedStartDate, 0, 0
                    )";

                var taskTitle = action.ActionTitle;
                var taskDesc = string.IsNullOrEmpty(action.ActionNote) 
                    ? "由週會臨時任務轉入。" 
                    : $"由週會臨時任務轉入。備註：{action.ActionNote}";

                var dueDate = action.DueDate;
                var plannedStartDate = action.DueDate.AddDays(-7);
                if (plannedStartDate > dueDate) plannedStartDate = dueDate;

                var taskId = await conn.QuerySingleAsync<int>(sqlTask, new 
                {
                    PhaseId = phaseId,
                    TaskTitle = taskTitle,
                    TaskDesc = taskDesc,
                    OwnerUserId = action.OwnerUserId,
                    DueDate = dueDate,
                    PlannedStartDate = plannedStartDate,
                    Priority = priority,
                    OperatorUserId = operatorUserId
                }, transaction: trans);

                // 3. Insert Status Log
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, NULL, 'Todo', N'由週會臨時任務轉入', @OperatorUserId, GETDATE())";
                await conn.ExecuteAsync(sqlLog, new { TaskId = taskId, OperatorUserId = operatorUserId }, transaction: trans);

                // 4. Update MPMS_MEETING_ACTION
                const string sqlUpdateAction = @"
                    UPDATE dbo.MPMS_MEETING_ACTION
                    SET task_id = @TaskId
                    WHERE action_id = @ActionId";
                await conn.ExecuteAsync(sqlUpdateAction, new { TaskId = taskId, ActionId = actionId }, transaction: trans);

                trans.Commit();
                return taskId;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
    }
}
