using System.Text.Json;
using System.Threading.Tasks;
using MPMS.Models;
using MPMS.Repositories;

namespace MPMS.Services
{
    public class AuditService
    {
        private readonly AuditLogRepository _auditLogRepository;

        public AuditService(AuditLogRepository auditLogRepository)
        {
            _auditLogRepository = auditLogRepository;
        }

        /// <summary>
        /// Logs an audit record for an operation.
        /// </summary>
        public async Task LogAsync(int actorUserId, string actionType, string targetTable, string targetId, object? oldValue = null, object? newValue = null)
        {
            var oldJson = oldValue != null ? JsonSerializer.Serialize(oldValue) : null;
            var newJson = newValue != null ? JsonSerializer.Serialize(newValue) : null;

            var log = new AuditLog
            {
                ActorUserId = actorUserId,
                ActionType = actionType,
                TargetTable = targetTable,
                TargetId = targetId,
                OldValueJson = oldJson,
                NewValueJson = newJson
            };

            await _auditLogRepository.CreateAuditLogAsync(log);
        }
    }
}
