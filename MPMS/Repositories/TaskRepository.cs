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
    public class TaskRepository : BaseRepository
    {
        public TaskRepository(IConfiguration configuration) : base(configuration)
        {
        }

        // Check if adding the proposed predecessor relationships creates a cycle
        public async Task<bool> CheckHasCycleAsync(int taskId, IEnumerable<int> proposedPredecessorIds, int? projectId = null)
        {
            using var conn = CreateConnection();
            if ((!projectId.HasValue || projectId.Value <= 0) && taskId > 0)
            {
                const string sqlProjId = "SELECT phase_id FROM dbo.MPMS_TASK WHERE task_id = @TaskId";
                var phaseId = await conn.QueryFirstOrDefaultAsync<int?>(sqlProjId, new { TaskId = taskId });
                if (phaseId.HasValue)
                {
                    const string sqlProj = "SELECT project_id FROM dbo.MPMS_PROJECT_PHASE WHERE phase_id = @PhaseId";
                    projectId = await conn.QueryFirstOrDefaultAsync<int?>(sqlProj, new { PhaseId = phaseId.Value });
                }
            }

            if (!projectId.HasValue || projectId.Value <= 0) return false;

            // Load all tasks for the project to build the graph
            const string sqlTasks = @"
                SELECT t.task_id 
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                WHERE ph.project_id = @ProjectId";
            var projectTasks = (await conn.QueryAsync<int>(sqlTasks, new { ProjectId = projectId.Value })).ToList();

            // Load all existing dependencies in the project
            const string sqlDeps = @"
                SELECT dep.task_id, dep.predecessor_task_id
                FROM dbo.MPMS_TASK_DEPENDENCY dep
                INNER JOIN dbo.MPMS_TASK t ON dep.task_id = t.task_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                WHERE ph.project_id = @ProjectId";
            var existingDeps = (await conn.QueryAsync<(int TaskId, int PredecessorTaskId)>(sqlDeps, new { ProjectId = projectId.Value })).ToList();

            // Build adjacency list for DFS
            var adj = new Dictionary<int, List<int>>();
            foreach (var tId in projectTasks)
            {
                adj[tId] = new List<int>();
            }

            // Populate existing edges, but exclude any existing edges for the task being updated
            foreach (var dep in existingDeps)
            {
                if (dep.TaskId != taskId)
                {
                    if (adj.ContainsKey(dep.PredecessorTaskId) && adj.ContainsKey(dep.TaskId))
                    {
                        adj[dep.PredecessorTaskId].Add(dep.TaskId);
                    }
                }
            }

            // Add proposed edges: proposedPredecessorId -> taskId
            foreach (var predId in proposedPredecessorIds)
            {
                if (predId == taskId) return true; // Self dependency

                if (adj.ContainsKey(predId) && adj.ContainsKey(taskId))
                {
                    adj[predId].Add(taskId);
                }
            }

            // Detect cycle using DFS
            var visited = new Dictionary<int, int>(); // 0 = unvisited, 1 = visiting, 2 = visited
            foreach (var tId in projectTasks)
            {
                visited[tId] = 0;
            }

            bool HasCycleDfs(int node)
            {
                visited[node] = 1; // visiting
                if (adj.ContainsKey(node))
                {
                    foreach (var neighbor in adj[node])
                    {
                        if (visited.GetValueOrDefault(neighbor) == 1)
                        {
                            return true; // cycle detected
                        }
                        if (visited.GetValueOrDefault(neighbor) == 0)
                        {
                            if (HasCycleDfs(neighbor)) return true;
                        }
                    }
                }
                visited[node] = 2; // visited
                return false;
            }

            foreach (var tId in projectTasks)
            {
                if (visited[tId] == 0)
                {
                    if (HasCycleDfs(tId)) return true;
                }
            }

            return false;
        }

        // Query Tasks for Dashboard - As Owner (Todo, Progress, Blocked, Reviewing)
        public async Task<IEnumerable<TaskModel>> GetTasksByOwnerAsync(int userId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.owner_user_id = @UserId AND t.task_status <> 'Done'
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { UserId = userId });
        }

        // Query Tasks for Dashboard - As Assistant
        public async Task<IEnumerable<TaskModel>> GetTasksByAssistAsync(int userId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                INNER JOIN dbo.MPMS_TASK_ASSIST ta ON t.task_id = ta.task_id
                WHERE ta.assist_user_id = @UserId AND t.task_status <> 'Done'
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { UserId = userId });
        }

        // Query Tasks for Dashboard - Reviewing Tasks where the user is assistant or PM but NOT owner
        public async Task<IEnumerable<TaskModel>> GetTasksForReviewAsync(int userId)
        {
            const string sql = @"
                SELECT DISTINCT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                LEFT JOIN dbo.MPMS_TASK_ASSIST ta ON t.task_id = ta.task_id
                WHERE t.task_status = 'Reviewing' 
                  AND t.owner_user_id <> @UserId
                  AND (ta.assist_user_id = @UserId OR p.pm_user_id = @UserId OR @UserId IN (SELECT user_id FROM dbo.MPMS_USER WHERE role_id = 1))
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { UserId = userId });
        }

        // Query Tasks under Project Phase
        public async Task<IEnumerable<TaskModel>> GetTasksByPhaseAsync(int phaseId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE t.phase_id = @PhaseId
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { PhaseId = phaseId });
        }

        // Query all Tasks under Project (across all phases)
        public async Task<IEnumerable<TaskModel>> GetTasksByProjectIdAsync(int projectId)
        {
            const string sql = @"
                SELECT t.*, ph.phase_name as PhaseName, u.user_name as OwnerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                WHERE ph.project_id = @ProjectId
                ORDER BY t.due_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskModel>(sql, new { ProjectId = projectId });
        }

        // Get Task by ID (including assistants, predecessors, and attachments)
        public async Task<TaskModel?> GetTaskByIdAsync(int taskId)
        {
            const string sqlTask = @"
                SELECT t.*, ph.phase_name as PhaseName, p.project_name as ProjectName, p.project_id as ProjectId, 
                       u.user_name as OwnerUserName, c.user_name as CreatorUserName,
                       u_rev.user_name as ReviewerUserName, u_bak.user_name as BackupReviewerUserName
                FROM dbo.MPMS_TASK t
                INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                INNER JOIN dbo.MPMS_USER u ON t.owner_user_id = u.user_id
                INNER JOIN dbo.MPMS_USER c ON t.created_by = c.user_id
                LEFT JOIN dbo.MPMS_USER u_rev ON t.reviewer_user_id = u_rev.user_id
                LEFT JOIN dbo.MPMS_USER u_bak ON t.backup_reviewer_user_id = u_bak.user_id
                WHERE t.task_id = @TaskId";

            using var conn = CreateConnection();
            var task = await conn.QueryFirstOrDefaultAsync<TaskModel>(sqlTask, new { TaskId = taskId });
            if (task == null) return null;

            // Fetch assistants
            const string sqlAssists = @"
                SELECT ta.assist_user_id, u.user_name
                FROM dbo.MPMS_TASK_ASSIST ta
                INNER JOIN dbo.MPMS_USER u ON ta.assist_user_id = u.user_id
                WHERE ta.task_id = @TaskId";
            var assists = await conn.QueryAsync(sqlAssists, new { TaskId = taskId });
            foreach (var a in assists)
            {
                task.AssistUserIds.Add((int)a.assist_user_id);
                task.AssistUserNames.Add((string)a.user_name);
            }

            // Fetch predecessors
            const string sqlPredecessors = @"
                SELECT dep.predecessor_task_id, t.task_title
                FROM dbo.MPMS_TASK_DEPENDENCY dep
                INNER JOIN dbo.MPMS_TASK t ON dep.predecessor_task_id = t.task_id
                WHERE dep.task_id = @TaskId";
            var predecessors = await conn.QueryAsync(sqlPredecessors, new { TaskId = taskId });
            foreach (var p in predecessors)
            {
                task.PredecessorTaskIds.Add((int)p.predecessor_task_id);
                task.PredecessorTaskTitles.Add((string)p.task_title);
            }

            // Fetch attachments
            const string sqlAtts = @"
                SELECT a.*, u.user_name as UploadUserName
                FROM dbo.MPMS_TASK_ATTACHMENT a
                INNER JOIN dbo.MPMS_USER u ON a.upload_user_id = u.user_id
                WHERE a.task_id = @TaskId AND a.is_void = 0";
            var atts = await conn.QueryAsync<TaskAttachment>(sqlAtts, new { TaskId = taskId });
            task.Attachments = atts.ToList();

            return task;
        }

        // Create Task (Transactional)
        public async Task<bool> CreateTaskAsync(TaskModel task)
        {
            if (task.PredecessorTaskIds != null && task.PredecessorTaskIds.Count > 0)
            {
                if (await CheckHasCycleAsync(0, task.PredecessorTaskIds, task.ProjectId))
                {
                    throw new InvalidOperationException("建立任務失敗：前置任務設定會產生循環相依！");
                }
            }

            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                const string sqlTask = @"
                    INSERT INTO dbo.MPMS_TASK (
                        phase_id, task_title, task_desc, owner_user_id, due_date, 
                        task_status, priority, created_by, updated_by, created_at, 
                        updated_at, reviewer_user_id, backup_reviewer_user_id, review_round,
                        planned_start_date, is_milestone, gantt_sort_no
                    )
                    OUTPUT INSERTED.task_id
                    VALUES (
                        @PhaseId, @TaskTitle, @TaskDesc, @OwnerUserId, @DueDate, 
                        'Todo', @Priority, @CreatedBy, @UpdatedBy, GETDATE(), 
                        GETDATE(), @ReviewerUserId, @BackupReviewerUserId, 0,
                        @PlannedStartDate, @IsMilestone, @GanttSortNo
                    )";

                var taskId = await conn.QuerySingleAsync<int>(sqlTask, task, transaction: trans);
                task.TaskId = taskId;

                if (task.AssistUserIds != null && task.AssistUserIds.Count > 0)
                {
                    const string sqlAssist = @"
                        INSERT INTO dbo.MPMS_TASK_ASSIST (task_id, assist_user_id, is_reviewer_candidate)
                        VALUES (@TaskId, @AssistUserId, 1)";
                    foreach (var assistId in task.AssistUserIds)
                    {
                        await conn.ExecuteAsync(sqlAssist, new { TaskId = taskId, AssistUserId = assistId }, transaction: trans);
                    }
                }

                if (task.PredecessorTaskIds != null && task.PredecessorTaskIds.Count > 0)
                {
                    const string sqlDep = @"
                        INSERT INTO dbo.MPMS_TASK_DEPENDENCY (task_id, predecessor_task_id, dependency_type, created_at)
                        VALUES (@TaskId, @PredecessorTaskId, 'FS', GETDATE())";
                    foreach (var predId in task.PredecessorTaskIds)
                    {
                        await conn.ExecuteAsync(sqlDep, new { TaskId = taskId, PredecessorTaskId = predId }, transaction: trans);
                    }
                }

                // Log initial status Todo
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, NULL, 'Todo', N'任務建立', @CreatedBy, GETDATE())";
                await conn.ExecuteAsync(sqlLog, new { TaskId = taskId, CreatedBy = task.CreatedBy }, transaction: trans);

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Update Task (Transactional)
        public async Task<bool> UpdateTaskAsync(TaskModel task)
        {
            if (task.PredecessorTaskIds != null && task.PredecessorTaskIds.Count > 0)
            {
                if (await CheckHasCycleAsync(task.TaskId, task.PredecessorTaskIds, task.ProjectId))
                {
                    throw new InvalidOperationException("更新任務失敗：前置任務設定會產生循環相依！");
                }
            }

            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                const string sqlTask = @"
                    UPDATE dbo.MPMS_TASK
                    SET phase_id = @PhaseId,
                        task_title = @TaskTitle,
                        task_desc = @TaskDesc,
                        owner_user_id = @OwnerUserId,
                        due_date = @DueDate,
                        priority = @Priority,
                        reviewer_user_id = @ReviewerUserId,
                        backup_reviewer_user_id = @BackupReviewerUserId,
                        planned_start_date = @PlannedStartDate,
                        is_milestone = @IsMilestone,
                        gantt_sort_no = @GanttSortNo,
                        updated_by = @UpdatedBy,
                        updated_at = GETDATE()
                    WHERE task_id = @TaskId AND row_version = @RowVersion";

                var rows = await conn.ExecuteAsync(sqlTask, task, transaction: trans);
                if (rows == 0)
                {
                    throw new System.Data.DBConcurrencyException("資料已被他人修改，請重新整理頁面。");
                }

                // Re-sync assistants
                const string sqlDeleteAssist = "DELETE FROM dbo.MPMS_TASK_ASSIST WHERE task_id = @TaskId";
                await conn.ExecuteAsync(sqlDeleteAssist, new { TaskId = task.TaskId }, transaction: trans);

                if (task.AssistUserIds != null && task.AssistUserIds.Count > 0)
                {
                    const string sqlInsertAssist = @"
                        INSERT INTO dbo.MPMS_TASK_ASSIST (task_id, assist_user_id, is_reviewer_candidate)
                        VALUES (@TaskId, @AssistUserId, 1)";
                    foreach (var assistId in task.AssistUserIds)
                    {
                        await conn.ExecuteAsync(sqlInsertAssist, new { TaskId = task.TaskId, AssistUserId = assistId }, transaction: trans);
                    }
                }

                // Re-sync predecessors
                const string sqlDeleteDeps = "DELETE FROM dbo.MPMS_TASK_DEPENDENCY WHERE task_id = @TaskId";
                await conn.ExecuteAsync(sqlDeleteDeps, new { TaskId = task.TaskId }, transaction: trans);

                if (task.PredecessorTaskIds != null && task.PredecessorTaskIds.Count > 0)
                {
                    const string sqlInsertDep = @"
                        INSERT INTO dbo.MPMS_TASK_DEPENDENCY (task_id, predecessor_task_id, dependency_type, created_at)
                        VALUES (@TaskId, @PredecessorTaskId, 'FS', GETDATE())";
                    foreach (var predId in task.PredecessorTaskIds)
                    {
                        await conn.ExecuteAsync(sqlInsertDep, new { TaskId = task.TaskId, PredecessorTaskId = predId }, transaction: trans);
                    }
                }

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Update Task Status & Log Status Change (Transactional)
        public async Task<bool> UpdateTaskStatusAsync(int taskId, string newStatus, string? reason, int operatorUserId, byte[] rowVersion)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // Fetch old status and current_review_id
                const string sqlGetOld = "SELECT task_status, current_review_id FROM dbo.MPMS_TASK WHERE task_id = @TaskId";
                var taskDetails = await conn.QueryFirstOrDefaultAsync<dynamic>(sqlGetOld, new { TaskId = taskId }, transaction: trans);
                if (taskDetails == null)
                {
                    trans.Rollback();
                    return false;
                }

                string oldStatus = taskDetails.task_status;
                int? currentReviewId = taskDetails.current_review_id;

                if (oldStatus == newStatus)
                {
                    trans.Commit();
                    return true;
                }

                // Update status in Task
                string sqlUpdate = @"
                    UPDATE dbo.MPMS_TASK
                    SET task_status = @NewStatus,
                        updated_by = @OperatorUserId,
                        updated_at = GETDATE()";

                if (newStatus == "Blocked")
                {
                    sqlUpdate += ", blocked_reason = @Reason";
                }
                else if (newStatus == "Done")
                {
                    sqlUpdate += ", done_at = GETDATE()";
                }

                sqlUpdate += " WHERE task_id = @TaskId AND row_version = @RowVersion";

                var rows = await conn.ExecuteAsync(sqlUpdate, new { TaskId = taskId, NewStatus = newStatus, Reason = reason, OperatorUserId = operatorUserId, RowVersion = rowVersion }, transaction: trans);
                if (rows == 0)
                {
                    throw new System.Data.DBConcurrencyException("資料已被他人修改，請重新整理頁面。");
                }

                // V0.5 Reviewing -> Blocked: Cancel current Pending review
                if (oldStatus == "Reviewing" && newStatus == "Blocked" && currentReviewId.HasValue)
                {
                    const string sqlCancelReview = @"
                        UPDATE dbo.MPMS_TASK_REVIEW
                        SET review_result = 'Cancelled',
                            reviewed_at = GETDATE(),
                            review_comment = @Reason
                        WHERE review_id = @CurrentReviewId";
                    await conn.ExecuteAsync(sqlCancelReview, new { CurrentReviewId = currentReviewId.Value, Reason = reason ?? "任務轉為卡關，審查中止。" }, transaction: trans);
                }

                // Insert Status Log
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, @OldStatus, @NewStatus, @Reason, @OperatorUserId, GETDATE())";
                await conn.ExecuteAsync(sqlLog, new { TaskId = taskId, OldStatus = oldStatus, NewStatus = newStatus, Reason = reason, OperatorUserId = operatorUserId }, transaction: trans);

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Add Task Attachment
        public async Task<bool> AddAttachmentAsync(TaskAttachment att)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_TASK_ATTACHMENT (task_id, blob_container, blob_path, original_file_name, stored_file_name, file_ext, file_size_bytes, content_type, summary_text, upload_user_id, uploaded_at, is_void, visibility_type, scan_status)
                VALUES (@TaskId, @BlobContainer, @BlobPath, @OriginalFileName, @StoredFileName, @FileExt, @FileSizeBytes, @ContentType, @SummaryText, @UploadUserId, GETDATE(), 0, @VisibilityType, 'Pending')";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, att);
            return rows > 0;
        }

        // Void Attachment (Admin only)
        public async Task<bool> VoidAttachmentAsync(int attachmentId, string reason, int operatorUserId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_TASK_ATTACHMENT
                SET is_void = 1,
                    void_reason = @Reason
                WHERE attachment_id = @AttachmentId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { AttachmentId = attachmentId, Reason = reason });
            return rows > 0;
        }

        // Submit Task to Review (check constraints inside transaction)
        public async Task<bool> SubmitToReviewAsync(int taskId, string completeSummary, int operatorUserId, byte[] rowVersion)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Verify attachment count
                const string sqlCheckAtt = "SELECT COUNT(*) FROM dbo.MPMS_TASK_ATTACHMENT WHERE task_id = @TaskId AND is_void = 0 AND scan_status = 'Passed'";
                var attCount = await conn.QuerySingleAsync<int>(sqlCheckAtt, new { TaskId = taskId }, transaction: trans);
                if (attCount < 1)
                {
                    trans.Rollback();
                    return false; // Error: must have at least 1 attachment
                }

                // 2. Fetch task details: reviewer, owner, review_round
                const string sqlGetTaskInfo = "SELECT owner_user_id, reviewer_user_id, review_round FROM dbo.MPMS_TASK WHERE task_id = @TaskId";
                var taskInfo = await conn.QuerySingleOrDefaultAsync<dynamic>(sqlGetTaskInfo, new { TaskId = taskId }, transaction: trans);
                if (taskInfo == null)
                {
                    trans.Rollback();
                    return false;
                }

                int? reviewerUserId = taskInfo.reviewer_user_id;
                int ownerUserId = taskInfo.owner_user_id;
                int currentRound = taskInfo.review_round;

                if (!reviewerUserId.HasValue)
                {
                    throw new InvalidOperationException("送審失敗！尚未指定主要審查人。");
                }

                if (reviewerUserId.Value == ownerUserId)
                {
                    throw new InvalidOperationException("送審失敗！主要審查人不得為該任務的負責人。");
                }

                // 3. Check for existing Pending review
                const string sqlCheckPending = "SELECT COUNT(*) FROM dbo.MPMS_TASK_REVIEW WHERE task_id = @TaskId AND review_result = 'Pending'";
                var pendingCount = await conn.QuerySingleAsync<int>(sqlCheckPending, new { TaskId = taskId }, transaction: trans);
                if (pendingCount > 0)
                {
                    throw new InvalidOperationException("送審失敗！此任務已在審查中。");
                }

                // 4. Calculate new round
                int newRound = currentRound + 1;

                // 5. Create new Pending review record
                const string sqlInsertReview = @"
                    INSERT INTO dbo.MPMS_TASK_REVIEW (task_id, reviewer_user_id, review_result, review_comment, reviewed_at, source_type, review_round)
                    OUTPUT INSERTED.review_id
                    VALUES (@TaskId, @ReviewerUserId, 'Pending', '', GETDATE(), 'PeerReview', @NewRound)";
                
                int reviewId = await conn.QuerySingleAsync<int>(sqlInsertReview, new { TaskId = taskId, ReviewerUserId = reviewerUserId.Value, NewRound = newRound }, transaction: trans);

                // 6. Update Task Status, round and current_review_id
                const string sqlUpdateTask = @"
                    UPDATE dbo.MPMS_TASK
                    SET task_status = 'Reviewing',
                        complete_summary = @CompleteSummary,
                        review_requested_at = GETDATE(),
                        current_review_id = @ReviewId,
                        review_round = @NewRound,
                        updated_by = @OperatorUserId,
                        updated_at = GETDATE()
                    WHERE task_id = @TaskId AND row_version = @RowVersion";

                var rows = await conn.ExecuteAsync(sqlUpdateTask, new { TaskId = taskId, CompleteSummary = completeSummary, ReviewId = reviewId, NewRound = newRound, OperatorUserId = operatorUserId, RowVersion = rowVersion }, transaction: trans);
                if (rows == 0)
                {
                    throw new System.Data.DBConcurrencyException("資料已被他人修改，請重新整理頁面。");
                }

                // 7. Insert Status Log
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, 'Progress', 'Reviewing', N'完工送審', @OperatorUserId, GETDATE())";
                await conn.ExecuteAsync(sqlLog, new { TaskId = taskId, OperatorUserId = operatorUserId }, transaction: trans);

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Peer Review: Approve / Reject (Transactional)
        public async Task<bool> ProcessReviewAsync(TaskReview review, byte[] taskRowVersion, int operatorUserId)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // Update Review Record with optimistic lock check
                const string sqlReview = @"
                    UPDATE dbo.MPMS_TASK_REVIEW
                    SET review_result = @ReviewResult,
                        review_comment = @ReviewComment,
                        reviewed_at = GETDATE()
                    WHERE review_id = @ReviewId AND row_version = @RowVersion";

                var reviewRows = await conn.ExecuteAsync(sqlReview, review, transaction: trans);
                if (reviewRows == 0)
                {
                    throw new System.Data.DBConcurrencyException("此審查已被他人處理或已取消，請重新整理頁面。");
                }

                // Query all active attachments for task and link them
                const string sqlGetAtts = "SELECT attachment_id FROM dbo.MPMS_TASK_ATTACHMENT WHERE task_id = @TaskId AND is_void = 0";
                var attIds = await conn.QueryAsync<int>(sqlGetAtts, new { TaskId = review.TaskId }, transaction: trans);

                const string sqlLink = @"
                    INSERT INTO dbo.MPMS_TASK_REVIEW_ATTACHMENT (review_id, attachment_id)
                    VALUES (@ReviewId, @AttachmentId)";
                foreach (var attId in attIds)
                {
                    await conn.ExecuteAsync(sqlLink, new { ReviewId = review.ReviewId, AttachmentId = attId }, transaction: trans);
                }

                // Update Task Status
                string newStatus = review.ReviewResult == "Approved" ? "Done" : "Progress";
                DateTime? doneAt = review.ReviewResult == "Approved" ? DateTime.Now : (DateTime?)null;

                string sqlUpdateTask = @"
                    UPDATE dbo.MPMS_TASK
                    SET task_status = @NewStatus,
                        done_at = @DoneAt,
                        updated_by = @OperatorUserId,
                        updated_at = GETDATE()
                    WHERE task_id = @TaskId AND row_version = @TaskRowVersion";

                var taskRows = await conn.ExecuteAsync(sqlUpdateTask, new { NewStatus = newStatus, DoneAt = doneAt, TaskId = review.TaskId, TaskRowVersion = taskRowVersion, OperatorUserId = operatorUserId }, transaction: trans);
                if (taskRows == 0)
                {
                    throw new System.Data.DBConcurrencyException("任務資料已被他人修改，請重新整理頁面。");
                }

                // Insert Status Log
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, 'Reviewing', @NewStatus, @Reason, @OperatorUserId, GETDATE())";
                
                string logReason = review.ReviewResult == "Approved" ? "審查通過" : "審查退回: " + review.ReviewComment;
                await conn.ExecuteAsync(sqlLog, new { TaskId = review.TaskId, NewStatus = newStatus, Reason = logReason, OperatorUserId = operatorUserId }, transaction: trans);

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Meeting Return: Reject a task from Reviewing or Done status (Transactional)
        public async Task<bool> MeetingReturnTaskAsync(int taskId, string comment, int reviewerUserId, byte[] rowVersion)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Get current task status and current_review_id
                const string sqlGetTask = "SELECT task_status, current_review_id, review_round FROM dbo.MPMS_TASK WHERE task_id = @TaskId";
                var taskInfo = await conn.QuerySingleOrDefaultAsync<dynamic>(sqlGetTask, new { TaskId = taskId }, transaction: trans);
                if (taskInfo == null)
                {
                    trans.Rollback();
                    return false;
                }

                string currentStatus = taskInfo.task_status;
                int? currentReviewId = taskInfo.current_review_id;
                int currentRound = taskInfo.review_round;

                int reviewIdToLink;

                // 2. Process Review Record based on status
                if (currentStatus == "Reviewing" && currentReviewId.HasValue)
                {
                    // Update existing Pending Review to Rejected
                    const string sqlUpdateReview = @"
                        UPDATE dbo.MPMS_TASK_REVIEW
                        SET review_result = 'Rejected',
                            review_comment = @Comment,
                            reviewed_at = GETDATE(),
                            source_type = 'MeetingReturn'
                        WHERE review_id = @ReviewId";
                    await conn.ExecuteAsync(sqlUpdateReview, new { ReviewId = currentReviewId.Value, Comment = comment }, transaction: trans);
                    reviewIdToLink = currentReviewId.Value;
                }
                else
                {
                    // Done status (or others): Insert a new Rejected Review
                    const string sqlInsertReview = @"
                        INSERT INTO dbo.MPMS_TASK_REVIEW (task_id, reviewer_user_id, review_result, review_comment, reviewed_at, source_type, review_round)
                        OUTPUT INSERTED.review_id
                        VALUES (@TaskId, @ReviewerUserId, 'Rejected', @Comment, GETDATE(), 'MeetingReturn', @Round)";
                    reviewIdToLink = await conn.QuerySingleAsync<int>(sqlInsertReview, new { TaskId = taskId, ReviewerUserId = reviewerUserId, Comment = comment, Round = currentRound }, transaction: trans);
                }

                // 3. Update Task Status to 'Progress' with row_version check
                const string sqlUpdateTask = @"
                    UPDATE dbo.MPMS_TASK
                    SET task_status = 'Progress',
                        done_at = NULL,
                        current_review_id = @ReviewId,
                        updated_by = @ReviewerUserId,
                        updated_at = GETDATE()
                    WHERE task_id = @TaskId AND row_version = @RowVersion";

                var rows = await conn.ExecuteAsync(sqlUpdateTask, new { TaskId = taskId, ReviewerUserId = reviewerUserId, ReviewId = reviewIdToLink, RowVersion = rowVersion }, transaction: trans);
                if (rows == 0)
                {
                    throw new System.Data.DBConcurrencyException("資料已被他人修改，請重新整理頁面。");
                }

                // 4. Insert Status Log
                const string sqlLog = @"
                    INSERT INTO dbo.MPMS_TASK_STATUS_LOG (task_id, old_status, new_status, change_reason, changed_by, changed_at)
                    VALUES (@TaskId, @OldStatus, 'Progress', @Reason, @ReviewerUserId, GETDATE())";
                string logReason = $"週會現場退回: {comment}";
                await conn.ExecuteAsync(sqlLog, new { TaskId = taskId, OldStatus = currentStatus, Reason = logReason, ReviewerUserId = reviewerUserId }, transaction: trans);

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Get Status Logs for a Task
        public async Task<IEnumerable<TaskStatusLog>> GetTaskStatusLogsAsync(int taskId)
        {
            const string sql = @"
                SELECT l.*, u.user_name as OperatorUserName
                FROM dbo.MPMS_TASK_STATUS_LOG l
                INNER JOIN dbo.MPMS_USER u ON l.changed_by = u.user_id
                WHERE l.task_id = @TaskId
                ORDER BY l.status_log_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskStatusLog>(sql, new { TaskId = taskId });
        }

        // Get Reviews for a Task
        public async Task<IEnumerable<TaskReview>> GetTaskReviewsAsync(int taskId)
        {
            const string sql = @"
                SELECT r.*, u.user_name as ReviewerUserName
                FROM dbo.MPMS_TASK_REVIEW r
                INNER JOIN dbo.MPMS_USER u ON r.reviewer_user_id = u.user_id
                WHERE r.task_id = @TaskId
                ORDER BY r.review_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskReview>(sql, new { TaskId = taskId });
        }

        // Get all active users (to assign as Owner or Assistants)
        public async Task<IEnumerable<User>> GetActiveUsersAsync()
        {
            const string sql = @"
                SELECT u.*, r.role_code as RoleCode, r.role_name as RoleName
                FROM dbo.MPMS_USER u
                INNER JOIN dbo.MPMS_ROLE r ON u.role_id = r.role_id
                WHERE u.is_active = 1
                ORDER BY u.user_name ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<User>(sql);
        }

        // Get Attachment by ID
        public async Task<TaskAttachment?> GetAttachmentByIdAsync(int attachmentId)
        {
            const string sql = "SELECT * FROM dbo.MPMS_TASK_ATTACHMENT WHERE attachment_id = @AttachmentId";
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<TaskAttachment>(sql, new { AttachmentId = attachmentId });
        }
    }
}
