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
    }
}
