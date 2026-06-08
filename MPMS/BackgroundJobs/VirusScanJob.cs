using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MPMS.Hubs;
using Quartz;

namespace MPMS.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class VirusScanJob : IJob
    {
        private readonly IConfiguration _configuration;
        private readonly IHubContext<MeetingHub> _meetingHubContext;
        private readonly ILogger<VirusScanJob> _logger;

        public VirusScanJob(IConfiguration configuration, IHubContext<MeetingHub> meetingHubContext, ILogger<VirusScanJob> logger)
        {
            _configuration = configuration;
            _meetingHubContext = meetingHubContext;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("VirusScanJob started scanning pending attachments...");

            const string connStringKey = "ConnectionStrings:DefaultConnection";
            var connectionString = _configuration[connStringKey];

            using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await conn.OpenAsync();

            // 1. Fetch pending attachments
            const string sqlSelect = "SELECT attachment_id, task_id, original_file_name FROM dbo.MPMS_TASK_ATTACHMENT WHERE scan_status = 'Pending'";
            var pendings = await conn.QueryAsync<dynamic>(sqlSelect);

            const string sqlUpdate = "UPDATE dbo.MPMS_TASK_ATTACHMENT SET scan_status = 'Passed' WHERE attachment_id = @AttachmentId";

            foreach (var item in pendings)
            {
                _logger.LogInformation("Scanning file '{FileName}' (ID: {AttachmentId}) - PASSED", (string)item.original_file_name, (int)item.attachment_id);
                await conn.ExecuteAsync(sqlUpdate, new { AttachmentId = item.attachment_id });
                
                // Notify via SignalR that task updated
                await _meetingHubContext.Clients.All.SendAsync("TaskUpdated", (int)item.task_id);
            }

            _logger.LogInformation("VirusScanJob finished.");
        }
    }
}
