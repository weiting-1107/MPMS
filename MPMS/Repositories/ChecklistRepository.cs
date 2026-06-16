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
    public class ChecklistRepository : BaseRepository
    {
        public ChecklistRepository(IConfiguration configuration) : base(configuration) { }

        public async Task<IEnumerable<TaskChecklist>> GetChecklistsByTaskIdAsync(int taskId)
        {
            const string sql = @"
                SELECT c.*, 
                       au.user_name as AssignedUserName,
                       du.user_name as DoneByUserName
                FROM dbo.MPMS_TASK_CHECKLIST c
                LEFT JOIN dbo.MPMS_USER au ON c.assigned_user_id = au.user_id
                LEFT JOIN dbo.MPMS_USER du ON c.done_by = du.user_id
                WHERE c.task_id = @TaskId
                ORDER BY c.sort_no ASC, c.checklist_id ASC";
            
            using var conn = CreateConnection();
            return await conn.QueryAsync<TaskChecklist>(sql, new { TaskId = taskId });
        }

        public async Task<TaskChecklist?> GetChecklistByIdAsync(long checklistId)
        {
            const string sql = @"
                SELECT c.*, 
                       au.user_name as AssignedUserName,
                       du.user_name as DoneByUserName
                FROM dbo.MPMS_TASK_CHECKLIST c
                LEFT JOIN dbo.MPMS_USER au ON c.assigned_user_id = au.user_id
                LEFT JOIN dbo.MPMS_USER du ON c.done_by = du.user_id
                WHERE c.checklist_id = @ChecklistId";
                
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<TaskChecklist>(sql, new { ChecklistId = checklistId });
        }

        public async Task<long> CreateChecklistAsync(TaskChecklist item)
        {
            const string sqlSort = "SELECT ISNULL(MAX(sort_no), 0) + 10 FROM dbo.MPMS_TASK_CHECKLIST WHERE task_id = @TaskId";
            
            const string sqlInsert = @"
                INSERT INTO dbo.MPMS_TASK_CHECKLIST (
                    task_id, checklist_title, checklist_desc, assigned_user_id, due_date, 
                    is_done, sort_no, note, created_by
                )
                OUTPUT INSERTED.checklist_id
                VALUES (
                    @TaskId, @ChecklistTitle, @ChecklistDesc, @AssignedUserId, @DueDate,
                    0, @SortNo, @Note, @CreatedBy
                )";

            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                int nextSortNo = await conn.ExecuteScalarAsync<int>(sqlSort, new { TaskId = item.TaskId }, transaction: trans);
                
                var newId = await conn.QuerySingleAsync<long>(sqlInsert, new {
                    TaskId = item.TaskId,
                    ChecklistTitle = item.ChecklistTitle,
                    ChecklistDesc = item.ChecklistDesc,
                    AssignedUserId = item.AssignedUserId,
                    DueDate = item.DueDate,
                    SortNo = nextSortNo,
                    Note = item.Note,
                    CreatedBy = item.CreatedBy
                }, transaction: trans);

                trans.Commit();
                return newId;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task<int> UpdateChecklistAsync(TaskChecklist item)
        {
            const string sql = @"
                UPDATE dbo.MPMS_TASK_CHECKLIST
                SET checklist_title = @ChecklistTitle,
                    checklist_desc = @ChecklistDesc,
                    assigned_user_id = @AssignedUserId,
                    due_date = @DueDate,
                    note = @Note,
                    updated_at_utc = SYSUTCDATETIME()
                WHERE checklist_id = @ChecklistId AND row_version = @RowVersion";

            using var conn = CreateConnection();
            return await conn.ExecuteAsync(sql, item);
        }

        public async Task<int> ToggleStatusAsync(long checklistId, bool isDone, int doneByUserId, byte[] rowVersion)
        {
            const string sql = @"
                UPDATE dbo.MPMS_TASK_CHECKLIST
                SET is_done = @IsDone,
                    done_by = @DoneBy,
                    done_at_utc = @DoneAtUtc,
                    updated_at_utc = SYSUTCDATETIME()
                WHERE checklist_id = @ChecklistId AND row_version = @RowVersion";

            using var conn = CreateConnection();
            return await conn.ExecuteAsync(sql, new {
                ChecklistId = checklistId,
                IsDone = isDone,
                DoneBy = isDone ? doneByUserId : (int?)null,
                DoneAtUtc = isDone ? (DateTime?)DateTime.UtcNow : null,
                RowVersion = rowVersion
            });
        }

        public async Task<int> DeleteChecklistAsync(long checklistId, byte[] rowVersion)
        {
            const string sql = @"
                DELETE FROM dbo.MPMS_TASK_CHECKLIST 
                WHERE checklist_id = @ChecklistId AND row_version = @RowVersion";

            using var conn = CreateConnection();
            return await conn.ExecuteAsync(sql, new { ChecklistId = checklistId, RowVersion = rowVersion });
        }

        public async Task<bool> UpdateOrderAsync(int taskId, List<long> sortedChecklistIds)
        {
            const string sqlUpdate = @"
                UPDATE dbo.MPMS_TASK_CHECKLIST
                SET sort_no = @SortNo,
                    updated_at_utc = SYSUTCDATETIME()
                WHERE checklist_id = @ChecklistId AND task_id = @TaskId";

            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                int currentSort = 10;
                foreach (var id in sortedChecklistIds)
                {
                    await conn.ExecuteAsync(sqlUpdate, new { SortNo = currentSort, ChecklistId = id, TaskId = taskId }, transaction: trans);
                    currentSort += 10;
                }
                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }
    }
}
