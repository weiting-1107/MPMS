using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using Microsoft.Data.SqlClient;

namespace MPMS.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class EmailSenderJob : IJob
    {
        private readonly string _connectionString;
        private readonly ILogger<EmailSenderJob> _logger;

        public EmailSenderJob(IConfiguration configuration, ILogger<EmailSenderJob> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is missing.");
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("EmailSenderJob running...");

            using var conn = new SqlConnection(_connectionString);
            const string sqlGetPending = @"
                SELECT email_log_id as EmailLogId, receiver_email as ReceiverEmail, 
                       subject as Subject, body as Body, retry_count as RetryCount
                FROM dbo.MPMS_EMAIL_LOG 
                WHERE send_status = 'Pending' AND retry_count < 3";

            var pendingEmails = await conn.QueryAsync(sqlGetPending);

            foreach (var email in pendingEmails)
            {
                int emailLogId = email.EmailLogId;
                string receiverEmail = email.ReceiverEmail;
                string subject = email.Subject;
                string body = email.Body;
                int retryCount = email.RetryCount;

                _logger.LogInformation($"[MOCK EMAIL SENDER] Processing email ID: {emailLogId}");

                try
                {
                    // Mock physical mail sending
                    _logger.LogInformation($@"
=========================================
[MOCK SMTP EMAIL SENDING]
To: {receiverEmail}
Subject: {subject}
Body:
{body}
=========================================");

                    // Update database to Sent
                    const string sqlUpdateSent = @"
                        UPDATE dbo.MPMS_EMAIL_LOG 
                        SET send_status = 'Sent',
                            sent_at = GETDATE(),
                            retry_count = @RetryCount
                        WHERE email_log_id = @EmailLogId";
                    
                    await conn.ExecuteAsync(sqlUpdateSent, new 
                    { 
                        EmailLogId = emailLogId, 
                        RetryCount = retryCount + 1 
                    });

                    _logger.LogInformation($"[MOCK EMAIL SENDER] Email ID: {emailLogId} marked as Sent.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[MOCK EMAIL SENDER] Failed to send email ID: {emailLogId}");

                    var newRetryCount = retryCount + 1;
                    var newStatus = newRetryCount >= 3 ? "Failed" : "Pending";

                    const string sqlUpdateFailed = @"
                        UPDATE dbo.MPMS_EMAIL_LOG 
                        SET send_status = @SendStatus,
                            error_message = @ErrorMessage,
                            retry_count = @RetryCount
                        WHERE email_log_id = @EmailLogId";

                    await conn.ExecuteAsync(sqlUpdateFailed, new
                    {
                        SendStatus = newStatus,
                        ErrorMessage = ex.Message,
                        RetryCount = newRetryCount,
                        EmailLogId = emailLogId
                    });
                }
            }
        }
    }
}
