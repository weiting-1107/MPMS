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
    }
}
