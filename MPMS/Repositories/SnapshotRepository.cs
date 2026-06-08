using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class SnapshotRepository : BaseRepository
    {
        public SnapshotRepository(IConfiguration configuration) : base(configuration)
        {
        }

        // Check if snapshot exists for a meeting
        public async Task<bool> CheckSnapshotExistsAsync(int meetingId)
        {
            const string sql = "SELECT COUNT(*) FROM dbo.MPMS_WEEKLY_SNAPSHOT WHERE meeting_id = @MeetingId";
            using var conn = CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>(sql, new { MeetingId = meetingId });
            return count > 0;
        }

        // Get maximum snapshot version for a meeting
        public async Task<int> GetLatestSnapshotVersionAsync(int meetingId)
        {
            const string sql = "SELECT ISNULL(MAX(snapshot_version_no), 0) FROM dbo.MPMS_WEEKLY_SNAPSHOT WHERE meeting_id = @MeetingId";
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int>(sql, new { MeetingId = meetingId });
        }

        // Seal Weekly Snapshot (Transactional)
        public async Task<int> SealSnapshotAsync(int? meetingId, int projectId, int sealedByUserId, string? correctionReason, string? snapshotNote = null)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Calculate Period and Week Code in Asia/Taipei TimeZone
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
                var utcNow = DateTime.UtcNow;
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
                
                int isoYear = System.Globalization.ISOWeek.GetYear(localNow);
                int weekNum = System.Globalization.ISOWeek.GetWeekOfYear(localNow);
                string weekCode = $"{isoYear}-W{weekNum:D2}";

                // Find period start (end date of last sealed snapshot, or project start date)
                const string sqlLastEnd = @"
                    SELECT TOP 1 period_end_at_utc 
                    FROM dbo.MPMS_WEEKLY_SNAPSHOT 
                    WHERE project_id = @ProjectId AND is_current_version = 1 
                    ORDER BY snapshot_version_no DESC";
                var lastPeriodEnd = await conn.QuerySingleOrDefaultAsync<DateTime?>(sqlLastEnd, new { ProjectId = projectId }, transaction: trans);

                DateTime periodStartUtc;
                if (lastPeriodEnd.HasValue)
                {
                    periodStartUtc = lastPeriodEnd.Value;
                }
                else
                {
                    // If no previous snapshot, use project start_date (interpreted in local timezone, converted to UTC)
                    const string sqlProjStart = "SELECT start_date FROM dbo.MPMS_PROJECT WHERE project_id = @ProjectId";
                    var projStartDate = await conn.QuerySingleAsync<DateTime>(sqlProjStart, new { ProjectId = projectId }, transaction: trans);
                    var localStart = new DateTime(projStartDate.Year, projStartDate.Month, projStartDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
                    periodStartUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
                }

                // 2. Determine version, type, and parent_snapshot_id
                const string sqlMaxVersion = @"
                    SELECT TOP 1 snapshot_id, snapshot_version_no 
                    FROM dbo.MPMS_WEEKLY_SNAPSHOT 
                    WHERE project_id = @ProjectId AND week_code = @WeekCode 
                    ORDER BY snapshot_version_no DESC";
                var prevSnapshot = await conn.QueryFirstOrDefaultAsync<dynamic>(sqlMaxVersion, new { ProjectId = projectId, WeekCode = weekCode }, transaction: trans);

                int snapshotVersionNo = 1;
                string snapshotType = "Normal";
                int? parentSnapshotId = null;

                if (prevSnapshot != null)
                {
                    snapshotVersionNo = (int)prevSnapshot.snapshot_version_no + 1;
                    snapshotType = "Correction";
                    parentSnapshotId = (int)prevSnapshot.snapshot_id;

                    // Set previous versions as not current
                    const string sqlDisableCurrent = @"
                        UPDATE dbo.MPMS_WEEKLY_SNAPSHOT
                        SET is_current_version = 0
                        WHERE project_id = @ProjectId AND week_code = @WeekCode";
                    await conn.ExecuteAsync(sqlDisableCurrent, new { ProjectId = projectId, WeekCode = weekCode }, transaction: trans);
                }

                // 3. Insert Weekly Snapshot Master Record
                const string sqlMaster = @"
                    INSERT INTO dbo.MPMS_WEEKLY_SNAPSHOT (
                        project_id, week_code, period_start_at_utc, period_end_at_utc, 
                        snapshot_version_no, snapshot_type, is_current_version, 
                        parent_snapshot_id, correction_reason, sealed_by, sealed_at_utc, 
                        snapshot_note, meeting_id
                    )
                    OUTPUT INSERTED.snapshot_id
                    VALUES (
                        @ProjectId, @WeekCode, @PeriodStartUtc, @PeriodEndUtc,
                        @SnapshotVersionNo, @SnapshotType, 1,
                        @ParentSnapshotId, @CorrectionReason, @SealedBy, @SealedAtUtc,
                        @SnapshotNote, @MeetingId
                    )";

                var snapshotId = await conn.QuerySingleAsync<int>(sqlMaster, new
                {
                    ProjectId = projectId,
                    WeekCode = weekCode,
                    PeriodStartUtc = periodStartUtc,
                    PeriodEndUtc = utcNow,
                    SnapshotVersionNo = snapshotVersionNo,
                    SnapshotType = snapshotType,
                    ParentSnapshotId = parentSnapshotId,
                    CorrectionReason = correctionReason,
                    SealedBy = sealedByUserId,
                    SealedAtUtc = utcNow,
                    SnapshotNote = snapshotNote,
                    MeetingId = meetingId
                }, transaction: trans);

                // 4. Copy MPMS_PROJECT to MPMS_SNAPSHOT_PROJECT
                const string sqlProj = @"
                    INSERT INTO dbo.MPMS_SNAPSHOT_PROJECT (snapshot_id, project_id, project_status, manual_progress_pct, progress_note)
                    SELECT @SnapshotId, project_id, project_status, manual_progress_pct, progress_note
                    FROM dbo.MPMS_PROJECT
                    WHERE project_id = @ProjectId";
                await conn.ExecuteAsync(sqlProj, new { SnapshotId = snapshotId, ProjectId = projectId }, transaction: trans);

                // 5. Copy MPMS_PROJECT_PHASE to MPMS_SNAPSHOT_PHASE
                const string sqlPhase = @"
                    INSERT INTO dbo.MPMS_SNAPSHOT_PHASE (snapshot_id, phase_id, manual_progress_pct, phase_status)
                    SELECT @SnapshotId, phase_id, manual_progress_pct, phase_status
                    FROM dbo.MPMS_PROJECT_PHASE
                    WHERE project_id = @ProjectId";
                await conn.ExecuteAsync(sqlPhase, new { SnapshotId = snapshotId, ProjectId = projectId }, transaction: trans);

                // 6. Copy MPMS_TASK to MPMS_SNAPSHOT_TASK
                const string sqlTask = @"
                    INSERT INTO dbo.MPMS_SNAPSHOT_TASK (
                        snapshot_id, task_id, task_status, owner_user_id, due_date, 
                        attachment_count, latest_review_result, blocked_reason,
                        planned_start_date, predecessor_summary
                    )
                    SELECT 
                        @SnapshotId, 
                        t.task_id, 
                        t.task_status, 
                        t.owner_user_id, 
                        t.due_date,
                        (SELECT COUNT(*) FROM dbo.MPMS_TASK_ATTACHMENT a WHERE a.task_id = t.task_id AND a.is_void = 0) as attachment_count,
                        (SELECT TOP 1 r.review_result FROM dbo.MPMS_TASK_REVIEW r WHERE r.task_id = t.task_id ORDER BY r.review_id DESC) as latest_review_result,
                        t.blocked_reason,
                        t.planned_start_date,
                        (
                            SELECT STRING_AGG(CAST(dep.predecessor_task_id AS VARCHAR), ',')
                            FROM dbo.MPMS_TASK_DEPENDENCY dep
                            WHERE dep.task_id = t.task_id
                        ) as predecessor_summary
                    FROM dbo.MPMS_TASK t
                    INNER JOIN dbo.MPMS_PROJECT_PHASE ph ON t.phase_id = ph.phase_id
                    WHERE ph.project_id = @ProjectId";
                await conn.ExecuteAsync(sqlTask, new { SnapshotId = snapshotId, ProjectId = projectId }, transaction: trans);

                trans.Commit();
                return snapshotId;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // ==================== Historical Trends Queries ====================

        // 1. Get Project Progress Trend Data
        public async Task<IEnumerable<dynamic>> GetProjectProgressTrendAsync(int projectId)
        {
            const string sql = @"
                SELECT s.week_code as Week, sp.manual_progress_pct as Progress
                FROM dbo.MPMS_SNAPSHOT_PROJECT sp
                INNER JOIN dbo.MPMS_WEEKLY_SNAPSHOT s ON sp.snapshot_id = s.snapshot_id
                WHERE s.is_current_version = 1 AND sp.project_id = @ProjectId
                ORDER BY s.period_end_at_utc ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync(sql, new { ProjectId = projectId });
        }

        // 2. Get Blocked Tasks Trend Data
        public async Task<IEnumerable<dynamic>> GetBlockedTasksTrendAsync(int? projectId)
        {
            const string sql = @"
                SELECT s.week_code as Week, COUNT(st.snapshot_task_id) as BlockedCount
                FROM dbo.MPMS_SNAPSHOT_TASK st
                INNER JOIN dbo.MPMS_WEEKLY_SNAPSHOT s ON st.snapshot_id = s.snapshot_id
                INNER JOIN dbo.MPMS_TASK t ON st.task_id = t.task_id
                INNER JOIN dbo.MPMS_PROJECT_PHASE m ON t.phase_id = m.phase_id
                WHERE s.is_current_version = 1 
                  AND st.task_status = 'Blocked'
                  AND (@ProjectId IS NULL OR m.project_id = @ProjectId)
                GROUP BY s.week_code, s.period_end_at_utc
                ORDER BY s.period_end_at_utc ASC";

            using var conn = CreateConnection();
            return await conn.QueryAsync(sql, new { ProjectId = projectId });
        }
    }
}
