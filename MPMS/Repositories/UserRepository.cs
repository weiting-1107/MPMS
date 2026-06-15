using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using MPMS.Models;

namespace MPMS.Repositories
{
    public class UserRepository : BaseRepository
    {
        public UserRepository(IConfiguration configuration) : base(configuration)
        {
        }

        public async Task<User?> GetUserByAccountAsync(string account)
        {
            const string sql = @"
                SELECT u.*, r.role_code as RoleCode, r.role_name as RoleName
                FROM dbo.MPMS_USER u
                INNER JOIN dbo.MPMS_ROLE r ON u.role_id = r.role_id
                WHERE u.account = @Account";

            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<User>(sql, new { Account = account });
        }

        public async Task<IEnumerable<User>> GetUsersAsync()
        {
            const string sql = @"
                SELECT u.*, r.role_code as RoleCode, r.role_name as RoleName
                FROM dbo.MPMS_USER u
                INNER JOIN dbo.MPMS_ROLE r ON u.role_id = r.role_id
                ORDER BY u.user_id DESC";

            using var conn = CreateConnection();
            return await conn.QueryAsync<User>(sql);
        }

        public async Task<IEnumerable<Role>> GetRolesAsync()
        {
            const string sql = "SELECT * FROM dbo.MPMS_ROLE WHERE is_active = 1";
            using var conn = CreateConnection();
            return await conn.QueryAsync<Role>(sql);
        }

        public async Task<bool> CreateUserAsync(User user)
        {
            const string sql = @"
                INSERT INTO dbo.MPMS_USER (account, password_hash, user_name, email, role_id, is_active, created_at, updated_at)
                VALUES (@Account, @PasswordHash, @UserName, @Email, @RoleId, @IsActive, GETDATE(), GETDATE())";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, user);
            return rows > 0;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            const string sql = @"
                UPDATE dbo.MPMS_USER
                SET user_name = @UserName,
                    email = @Email,
                    role_id = @RoleId,
                    is_active = @IsActive,
                    updated_at = GETDATE()
                WHERE user_id = @UserId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, user);
            return rows > 0;
        }

        public async Task UpdateLastLoginAsync(int userId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_USER
                SET last_login_at = GETDATE()
                WHERE user_id = @UserId";

            using var conn = CreateConnection();
            await conn.ExecuteAsync(sql, new { UserId = userId });
        }

        public async Task<bool> UpdatePasswordAsync(int userId, string passwordHash)
        {
            const string sql = @"
                UPDATE dbo.MPMS_USER
                SET password_hash = @PasswordHash,
                    updated_at = GETDATE()
                WHERE user_id = @UserId";

            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { UserId = userId, PasswordHash = passwordHash });
            return rows > 0;
        }

        /// <summary>
        /// 檢查使用者是否有不可刪除的關聯資料（任務、專案、週會等）。
        /// 回傳描述衝突的字串；若可以安全刪除則回傳 null。
        /// </summary>
        public async Task<string?> CheckUserDependenciesAsync(int userId)
        {
            using var conn = CreateConnection();

            // 檢查是否為 PM 負責的專案
            var projectCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MPMS_PROJECT WHERE pm_user_id = @UserId", new { UserId = userId });
            if (projectCount > 0)
                return $"此使用者目前是 {projectCount} 個專案的負責 PM，請先將這些專案的 PM 移交給其他人員。";

            // 檢查是否有被指派的任務（擁有者 或 審查者）
            var taskCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MPMS_TASK WHERE owner_user_id = @UserId OR reviewer_user_id = @UserId OR backup_reviewer_user_id = @UserId",
                new { UserId = userId });
            if (taskCount > 0)
                return $"此使用者目前負責或審查 {taskCount} 筆任務，請先將這些任務重新指派給其他人員。";

            // 協辦人
            var assistCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MPMS_TASK_ASSIST WHERE assist_user_id = @UserId", new { UserId = userId });
            if (assistCount > 0)
                return $"此使用者是 {assistCount} 筆任務的協辦人，請先移除協辦人關聯。";

            // 週會主持人
            var meetingCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MPMS_MEETING WHERE host_user_id = @UserId", new { UserId = userId });
            if (meetingCount > 0)
                return $"此使用者是 {meetingCount} 場週會的主持人，請先將週會主持人更換為其他人員。";

            // Action Item 負責人
            var actionCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.MPMS_MEETING_ACTION WHERE owner_user_id = @UserId AND task_id IS NULL", new { UserId = userId });
            if (actionCount > 0)
                return $"此使用者有 {actionCount} 筆尚未完成的會議派工項目 (Action Item)，請先處理或轉移。";

            return null; // 可以安全刪除
        }

        /// <summary>
        /// 刪除使用者：先清除所有相關歷史紀錄，再刪除帳號。
        /// 前提：已通過 CheckUserDependenciesAsync 確認無業務依賴。
        /// </summary>
        public async Task<bool> DeleteUserAsync(int userId)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. 刪除使用者收到的通知記錄
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_NOTIFICATION WHERE receiver_user_id = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 2. 刪除此使用者觸發的稽核日誌（純歷史記錄，不影響業務）
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_AUDIT_LOG WHERE actor_user_id = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 3. 清除任務狀態變更日誌中的操作者（因 CHECK 失敗，改刪除含此使用者的紀錄）
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_TASK_STATUS_LOG WHERE changed_by = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 4. 清除任務附件上傳者（純記錄，刪除上傳者資訊列）
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_TASK_ATTACHMENT WHERE upload_user_id = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 5. 清除任務審查紀錄中的審查者
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_TASK_REVIEW WHERE reviewer_user_id = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 6. 清除週快照中 sealed_by 為此使用者的紀錄
                await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_WEEKLY_SNAPSHOT WHERE sealed_by = @UserId",
                    new { UserId = userId }, transaction: trans);

                // 7. 刪除使用者本身
                var rows = await conn.ExecuteAsync(
                    "DELETE FROM dbo.MPMS_USER WHERE user_id = @UserId",
                    new { UserId = userId }, transaction: trans);

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
