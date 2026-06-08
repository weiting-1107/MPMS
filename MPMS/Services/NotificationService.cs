using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using MPMS.Hubs;
using MPMS.Models;
using MPMS.Repositories;

namespace MPMS.Services
{
    public class NotificationService : BaseRepository
    {
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(
            IConfiguration configuration,
            IHubContext<NotificationHub> hubContext) : base(configuration)
        {
            _hubContext = hubContext;
        }

        // Core method: Create an in-app notification and write to the email logs.
        public async Task<bool> SendNotificationAsync(int receiverUserId, string eventType, string title, string message, string? refType = null, int? refId = null, bool sendEmail = true)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                // 1. Insert In-App Notification
                const string sqlNotif = @"
                    INSERT INTO dbo.MPMS_NOTIFICATION (receiver_user_id, event_type, title, message, ref_type, ref_id, is_read, created_at)
                    VALUES (@ReceiverUserId, @EventType, @Title, @Message, @RefType, @RefId, 0, GETDATE())";
                await conn.ExecuteAsync(sqlNotif, new
                {
                    ReceiverUserId = receiverUserId,
                    EventType = eventType,
                    Title = title,
                    Message = message,
                    RefType = refType,
                    RefId = refId
                }, transaction: trans);

                // 2. Fetch User Email
                const string sqlEmail = "SELECT email FROM dbo.MPMS_USER WHERE user_id = @UserId";
                var email = await conn.QuerySingleOrDefaultAsync<string>(sqlEmail, new { UserId = receiverUserId }, transaction: trans);

                if (sendEmail && !string.IsNullOrEmpty(email))
                {
                    // 3. Insert Email Log (Pending status)
                    const string sqlEmailLog = @"
                        INSERT INTO dbo.MPMS_EMAIL_LOG (receiver_email, subject, body, send_status, retry_count, error_message, sent_at, created_at)
                        VALUES (@ReceiverEmail, @Subject, @Body, 'Pending', 0, NULL, NULL, GETDATE())";
                    
                    var subject = $"[MPMS 系統通知] {title}";
                    var body = $"您好：\n\n這是一封來自 MPMS 系統的自動通知信。\n\n【通知事件】{title}\n【詳細內容】\n{message}\n\n請登入 MPMS 系統查閱詳情。\n\n此信件為系統自動發送，請勿直接回覆。";
                    
                    await conn.ExecuteAsync(sqlEmailLog, new
                    {
                        ReceiverEmail = email,
                        Subject = subject,
                        Body = body
                     }, transaction: trans);
                }

                trans.Commit();

                // 4. Push real-time SignalR notification if client connected
                await _hubContext.Clients.User(receiverUserId.ToString()).SendAsync("ReceiveNotification", new
                {
                    title = title,
                    message = message
                });

                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // Get all notifications for user
        public async Task<IEnumerable<Notification>> GetUserNotificationsAsync(int userId)
        {
            const string sql = @"
                SELECT * FROM dbo.MPMS_NOTIFICATION 
                WHERE receiver_user_id = @UserId 
                ORDER BY created_at DESC, notification_id DESC";
            using var conn = CreateConnection();
            return await conn.QueryAsync<Notification>(sql, new { UserId = userId });
        }

        // Get unread count
        public async Task<int> GetUnreadCountAsync(int userId)
        {
            const string sql = "SELECT COUNT(*) FROM dbo.MPMS_NOTIFICATION WHERE receiver_user_id = @UserId AND is_read = 0";
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId });
        }

        // Mark single notification as read
        public async Task<bool> MarkAsReadAsync(int notificationId, int userId)
        {
            const string sql = @"
                UPDATE dbo.MPMS_NOTIFICATION 
                SET is_read = 1 
                WHERE notification_id = @NotificationId AND receiver_user_id = @UserId";
            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { NotificationId = notificationId, UserId = userId });
            return rows > 0;
        }

        // Mark all notifications as read
        public async Task<bool> MarkAllAsReadAsync(int userId)
        {
            const string sql = "UPDATE dbo.MPMS_NOTIFICATION SET is_read = 1 WHERE receiver_user_id = @UserId";
            using var conn = CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { UserId = userId });
            return rows > 0;
        }
    }
}
