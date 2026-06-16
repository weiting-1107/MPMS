    using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class ProjectRepository : BaseRepository
    {
        public ProjectRepository(IConfiguration configuration) : base(configuration)
        {
        }

        public async Task<IEnumerable<Project>> GetProjectsAsync()
        {
            const string sql = @"
                SELECT p.*, u.user_name as PmUserName, u.email as PmEmail
                FROM dbo.MPMS_PROJECT p
                INNER JOIN dbo.MPMS_USER u ON p.pm_user_id = u.user_id
                ORDER BY p.project_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<Project>(sql);
        }

        public async Task<Project?> GetProjectByIdAsync(int projectId)
        {
            const string sql = @"
                SELECT p.*, u.user_name as PmUserName, u.email as PmEmail
                FROM dbo.MPMS_PROJECT p
                INNER JOIN dbo.MPMS_USER u ON p.pm_user_id = u.user_id
                WHERE p.project_id = @ProjectId";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Project>(sql, new { ProjectId = projectId });
        }

        public async Task<Project?> GetProjectByCodeAsync(string projectCode)
        {
            const string sql = @"
                SELECT p.*, u.user_name as PmUserName, u.email as PmEmail
                FROM dbo.MPMS_PROJECT p
                INNER JOIN dbo.MPMS_USER u ON p.pm_user_id = u.user_id
                WHERE p.project_code = @ProjectCode";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<Project>(sql, new { ProjectCode = projectCode });
        }

        public async Task<bool> CreateProjectAsync(Project project)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_PROJECT (project_code, project_name, project_desc, pm_user_id, start_date, end_date, manual_progress_pct, progress_note, project_status, is_active, created_at, updated_at)
                VALUES (@ProjectCode, @ProjectName, @ProjectDesc, @PmUserId, @StartDate, @EndDate, @ManualProgressPct, @ProgressNote, @ProjectStatus, @IsActive, GETDATE(), GETDATE())";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, project);
            return rows > 0;
        }

        public async Task<bool> UpdateProjectAsync(Project project)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT
                SET project_name = @ProjectName,
                    project_desc = @ProjectDesc,
                    pm_user_id = @PmUserId,
                    start_date = @StartDate,
                    end_date = @EndDate,
                    project_status = @ProjectStatus,
                    is_active = @IsActive,
                    updated_at = GETDATE()
                WHERE project_id = @ProjectId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, project);
            return rows > 0;
        }

        public async Task<bool> UpdateProjectProgressAsync(int projectId, decimal progress, string? note)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT
                SET manual_progress_pct = @Progress,
                    progress_note = @Note,
                    updated_at = GETDATE()
                WHERE project_id = @ProjectId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { ProjectId = projectId, Progress = progress, Note = note });
            return rows > 0;
        }

        public async Task<IEnumerable<User>> GetPmCandidatesAsync()
        {
            const string sql = @"
                SELECT u.*, r.role_code as RoleCode, r.role_name as RoleName
                FROM dbo.MPMS_USER u
                INNER JOIN dbo.MPMS_ROLE r ON u.role_id = r.role_id
                WHERE r.role_code IN ('ADMIN', 'PM') AND u.is_active = 1";

            using var conn = CreateConnection();
            return await conn.QueryAsync<User>(sql);
        }

        public async Task<bool> UpdateProjectPmAsync(int projectId, int newPmUserId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT
                SET pm_user_id = @NewPmUserId,
                    updated_at = GETDATE()
                WHERE project_id = @ProjectId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { ProjectId = projectId, NewPmUserId = newPmUserId });
            return rows > 0;
        }

        // Delete project and all children (phases, tasks, logs, reviews, attachments, predecessors) in transaction
        public async Task<bool> DeleteProjectAsync(int projectId)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Unlink meetings
                await conn.ExecuteAsync("UPDATE dbo.MPMS_MEETING SET project_id = NULL WHERE project_id = @ProjectId", new { ProjectId = projectId }, transaction: trans);

                // 2. Unlink parent snapshots and delete snapshots (cascades task/milestone/project snapshots)
                await conn.ExecuteAsync("UPDATE dbo.MPMS_WEEKLY_SNAPSHOT SET parent_snapshot_id = NULL WHERE project_id = @ProjectId", new { ProjectId = projectId }, transaction: trans);
                await conn.ExecuteAsync("DELETE FROM dbo.MPMS_WEEKLY_SNAPSHOT WHERE project_id = @ProjectId", new { ProjectId = projectId }, transaction: trans);

                // 3. Delete phase snapshots
                await conn.ExecuteAsync("DELETE FROM dbo.MPMS_SNAPSHOT_PHASE WHERE phase_id IN (SELECT phase_id FROM dbo.MPMS_PROJECT_PHASE WHERE project_id = @ProjectId)", new { ProjectId = projectId }, transaction: trans);

                // 4. Get all phase IDs under this project
                var phaseIds = (await conn.QueryAsync<int>(
                    "SELECT phase_id FROM dbo.MPMS_PROJECT_PHASE WHERE project_id = @ProjectId",
                    new { ProjectId = projectId }, transaction: trans)).ToList();

                if (phaseIds.Any())
                {
                    // 5. Get all task IDs under those phases
                    var taskIds = (await conn.QueryAsync<int>(
                        "SELECT task_id FROM dbo.MPMS_TASK WHERE phase_id IN @PhaseIds",
                        new { PhaseIds = phaseIds }, transaction: trans)).ToList();

                    if (taskIds.Any())
                    {
                        // 6. Break task review and block circular references
                        await conn.ExecuteAsync("UPDATE dbo.MPMS_TASK SET current_review_id = NULL, current_block_id = NULL WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);

                        // 7. Delete task reviews attachments association
                        await conn.ExecuteAsync(@"
                            DELETE FROM dbo.MPMS_TASK_REVIEW_ATTACHMENT 
                            WHERE review_id IN (SELECT review_id FROM dbo.MPMS_TASK_REVIEW WHERE task_id IN @Ids)", 
                            new { Ids = taskIds }, transaction: trans);

                        await conn.ExecuteAsync(@"
                            DELETE FROM dbo.MPMS_TASK_REVIEW_ATTACHMENT 
                            WHERE attachment_id IN (SELECT attachment_id FROM dbo.MPMS_TASK_ATTACHMENT WHERE task_id IN @Ids)", 
                            new { Ids = taskIds }, transaction: trans);

                        // Unlink tasks from meeting action items
                        await conn.ExecuteAsync("UPDATE dbo.MPMS_MEETING_ACTION SET task_id = NULL WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);

                        // 8. Delete related records for those tasks
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_STATUS_LOG WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_REVIEW WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_ATTACHMENT WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_DEPENDENCY WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_DEPENDENCY WHERE predecessor_task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_CHECKLIST WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        
                        // Delete block comments and block logs
                        await conn.ExecuteAsync(@"
                            DELETE FROM dbo.MPMS_TASK_BLOCK_COMMENT 
                            WHERE block_id IN (SELECT block_id FROM dbo.MPMS_TASK_BLOCK_LOG WHERE task_id IN @Ids)", 
                            new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_BLOCK_LOG WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);

                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_ASSIST WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_SNAPSHOT_TASK WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                        await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    }

                    // 9. Delete phases
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_PROJECT_PHASE WHERE project_id = @ProjectId", new { ProjectId = projectId }, transaction: trans);
                }

                // 10. Delete project itself
                var rows = await conn.ExecuteAsync("DELETE FROM dbo.MPMS_PROJECT WHERE project_id = @ProjectId", new { ProjectId = projectId }, transaction: trans);

                trans.Commit();
                return rows > 0;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
    }
}
