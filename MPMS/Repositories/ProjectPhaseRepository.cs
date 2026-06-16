using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class ProjectPhaseRepository : BaseRepository
    {
        public ProjectPhaseRepository(IConfiguration configuration) : base(configuration)
        {
        }

        public async Task<IEnumerable<ProjectPhase>> GetPhasesAsync()
        {
            const string sql = @"
                SELECT ph.*, p.project_name as ProjectName, p.project_code as ProjectCode
                FROM dbo.MPMS_PROJECT_PHASE ph
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                ORDER BY p.project_id DESC, ph.sort_no ASC, ph.start_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<ProjectPhase>(sql);
        }

        public async Task<IEnumerable<ProjectPhase>> GetPhasesByProjectIdAsync(int projectId)
        {
            const string sql = @"
                SELECT ph.*, p.project_name as ProjectName, p.project_code as ProjectCode
                FROM dbo.MPMS_PROJECT_PHASE ph
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                WHERE ph.project_id = @ProjectId
                ORDER BY ph.sort_no ASC, ph.start_date ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<ProjectPhase>(sql, new { ProjectId = projectId });
        }

        public async Task<ProjectPhase?> GetPhaseByIdAsync(int phaseId)
        {
            const string sql = @"
                SELECT ph.*, p.project_name as ProjectName, p.project_code as ProjectCode
                FROM dbo.MPMS_PROJECT_PHASE ph
                INNER JOIN dbo.MPMS_PROJECT p ON ph.project_id = p.project_id
                WHERE ph.phase_id = @PhaseId";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ProjectPhase>(sql, new { PhaseId = phaseId });
        }

        public async Task<bool> CreatePhaseAsync(ProjectPhase phase)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_PROJECT_PHASE (project_id, phase_name, start_date, end_date, sort_no, manual_progress_pct, phase_status, created_at, updated_at)
                VALUES (@ProjectId, @PhaseName, @StartDate, @EndDate, @SortNo, @ManualProgressPct, @PhaseStatus, GETDATE(), GETDATE())";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, phase);
            return rows > 0;
        }

        public async Task<bool> UpdatePhaseAsync(ProjectPhase phase)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT_PHASE
                SET phase_name = @PhaseName,
                    start_date = @StartDate,
                    end_date = @EndDate,
                    sort_no = @SortNo,
                    phase_status = @PhaseStatus,
                    updated_at = GETDATE()
                WHERE phase_id = @PhaseId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, phase);
            return rows > 0;
        }

        public async Task<bool> UpdatePhaseProgressAsync(int phaseId, decimal progress, string status)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT_PHASE
                SET manual_progress_pct = @Progress,
                    phase_status = @Status,
                    updated_at = GETDATE()
                WHERE phase_id = @PhaseId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new 
            { 
                PhaseId = phaseId, 
                Progress = progress, 
                Status = status
            });
            return rows > 0;
        }

        public async Task<bool> UpdateProgressModeAsync(int phaseId, string progressMode)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT_PHASE
                SET progress_mode = @ProgressMode,
                    updated_at = GETDATE()
                WHERE phase_id = @PhaseId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { PhaseId = phaseId, ProgressMode = progressMode });
            return rows > 0;
        }

        // Recalculate phase progress based on average task progress (used for Auto mode)
        public async Task RecalculatePhaseProgressAsync(int phaseId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_PROJECT_PHASE
                SET manual_progress_pct = (
                    SELECT ISNULL(AVG(CAST(manual_progress_pct AS FLOAT)), 0)
                    FROM dbo.MPMS_TASK
                    WHERE phase_id = @PhaseId
                ),
                updated_at = GETDATE()
                WHERE phase_id = @PhaseId AND progress_mode = 'Auto'";

            using var conn = CreateConnection();
            await conn.ExecuteAsync(sql, new { PhaseId = phaseId });
        }

        // Delete phase and all its tasks (cascade) in a transaction
        public async Task<bool> DeletePhaseAsync(int phaseId)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Get all task IDs under this phase
                var taskIds = (await conn.QueryAsync<int>(
                    "SELECT task_id FROM dbo.MPMS_TASK WHERE phase_id = @PhaseId",
                    new { PhaseId = phaseId }, transaction: trans)).ToList();

                if (taskIds.Any())
                {
                    // Break task review circular reference
                    await conn.ExecuteAsync("UPDATE dbo.MPMS_TASK SET current_review_id = NULL WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);

                    // Delete task reviews attachments association
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

                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_STATUS_LOG WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_REVIEW WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_ATTACHMENT WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_DEPENDENCY WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_DEPENDENCY WHERE predecessor_task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK_ASSIST WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_SNAPSHOT_TASK WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                    await conn.ExecuteAsync("DELETE FROM dbo.MPMS_TASK WHERE task_id IN @Ids", new { Ids = taskIds }, transaction: trans);
                }

                // 2. Delete the phase itself
                var rows = await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_PROJECT_PHASE WHERE phase_id = @PhaseId",
                    new { PhaseId = phaseId }, transaction: trans);

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
