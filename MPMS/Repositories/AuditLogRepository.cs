using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class AuditLogRepository : BaseRepository
    {
        public AuditLogRepository(IConfiguration configuration) : base(configuration)
        {
        }

        public async Task<bool> CreateAuditLogAsync(AuditLog log)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_AUDIT_LOG (actor_user_id, action_type, target_table, target_id, old_value_json, new_value_json, created_at)
                VALUES (@ActorUserId, @ActionType, @TargetTable, @TargetId, @OldValueJson, @NewValueJson, GETDATE())";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, log);
            return rows > 0;
        }

        public async Task<IEnumerable<AuditLog>> GetAuditLogsAsync()
        {
            const string sql = @"
                SELECT a.*, u.account as ActorAccount, u.user_name as ActorUserName
                FROM dbo.MPMS_AUDIT_LOG a
                INNER JOIN dbo.MPMS_USER u ON a.actor_user_id = u.user_id
                ORDER BY a.audit_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<AuditLog>(sql);
        }
    }
}
