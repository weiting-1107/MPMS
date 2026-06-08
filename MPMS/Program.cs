using MPMS.Repositories;
using MPMS.Services;
using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication.Cookies;
using Quartz;

namespace MPMS;

public class Program
{
    public static void Main(string[] args)
    {
        // Enable Dapper snake_case column to PascalCase property mapping
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllersWithViews();
        
        // Register Repositories
        builder.Services.AddScoped<UserRepository>();
        builder.Services.AddScoped<AuditLogRepository>();
        builder.Services.AddScoped<ProjectRepository>();
        builder.Services.AddScoped<ProjectPhaseRepository>();
        builder.Services.AddScoped<TaskRepository>();
        builder.Services.AddScoped<MeetingRepository>();
        builder.Services.AddScoped<SnapshotRepository>();

        // Register Services
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<NotificationService>();
        builder.Services.AddScoped<MPMS.Services.Storage.IAttachmentStorageService, MPMS.Services.Storage.LocalAttachmentStorageService>();

        // Add SignalR
        builder.Services.AddSignalR();

        // Add Quartz.NET Services
        builder.Services.AddQuartz(q =>
        {
            var emailJobKey = new JobKey("EmailSenderJob");
            q.AddJob<MPMS.BackgroundJobs.EmailSenderJob>(opts => opts.WithIdentity(emailJobKey));
            q.AddTrigger(opts => opts
                .ForJob(emailJobKey)
                .WithIdentity("EmailSenderJob-trigger")
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(30).RepeatForever()));

            var overdueJobKey = new JobKey("OverdueTaskJob");
            q.AddJob<MPMS.BackgroundJobs.OverdueTaskJob>(opts => opts.WithIdentity(overdueJobKey));
            q.AddTrigger(opts => opts
                .ForJob(overdueJobKey)
                .WithIdentity("OverdueTaskJob-trigger")
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(60).RepeatForever()));

            var virusScanJobKey = new JobKey("VirusScanJob");
            q.AddJob<MPMS.BackgroundJobs.VirusScanJob>(opts => opts.WithIdentity(virusScanJobKey));
            q.AddTrigger(opts => opts
                .ForJob(virusScanJobKey)
                .WithIdentity("VirusScanJob-trigger")
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever()));
        });

        builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        // Add Cookie Authentication
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromHours(4);
            });

        var app = builder.Build();

        // Test Database Connection at startup
        using (var scope = app.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var userRepo = scope.ServiceProvider.GetRequiredService<UserRepository>();
            try
            {
                logger.LogInformation("Testing database connection to local SQL Server...");
                var users = userRepo.GetUsersAsync().GetAwaiter().GetResult();
                logger.LogInformation($"Database connection successful! Found {users.Count()} users in database.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to connect to the database.");
            }
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        // app.UseHttpsRedirection();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapHub<MPMS.Hubs.MeetingHub>("/meetingHub");
        app.MapHub<MPMS.Hubs.NotificationHub>("/notificationHub");
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        app.Run();
    }
}

