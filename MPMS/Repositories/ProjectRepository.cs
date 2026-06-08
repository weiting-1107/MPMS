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
    }
}
