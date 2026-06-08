-- MPMS (Multi Project Management System) Database Schema (V0.1)
-- Target Database: SQL Server / LocalDB

USE master;
GO

-- 1. Create Database if not exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MPMS')
BEGIN
    CREATE DATABASE MPMS;
END
GO

USE MPMS;
GO

-- 2. Clean up existing tables (in reverse dependency order)
IF OBJECT_ID('dbo.MPMS_AUDIT_LOG', 'U') IS NOT NULL DROP TABLE dbo.MPMS_AUDIT_LOG;
IF OBJECT_ID('dbo.MPMS_EMAIL_LOG', 'U') IS NOT NULL DROP TABLE dbo.MPMS_EMAIL_LOG;
IF OBJECT_ID('dbo.MPMS_NOTIFICATION', 'U') IS NOT NULL DROP TABLE dbo.MPMS_NOTIFICATION;
IF OBJECT_ID('dbo.MPMS_SNAPSHOT_TASK', 'U') IS NOT NULL DROP TABLE dbo.MPMS_SNAPSHOT_TASK;
IF OBJECT_ID('dbo.MPMS_SNAPSHOT_MILESTONE', 'U') IS NOT NULL DROP TABLE dbo.MPMS_SNAPSHOT_MILESTONE;
IF OBJECT_ID('dbo.MPMS_SNAPSHOT_PROJECT', 'U') IS NOT NULL DROP TABLE dbo.MPMS_SNAPSHOT_PROJECT;
IF OBJECT_ID('dbo.MPMS_WEEKLY_SNAPSHOT', 'U') IS NOT NULL DROP TABLE dbo.MPMS_WEEKLY_SNAPSHOT;
IF OBJECT_ID('dbo.MPMS_MEETING_ACTION', 'U') IS NOT NULL DROP TABLE dbo.MPMS_MEETING_ACTION;
IF OBJECT_ID('dbo.MPMS_MEETING', 'U') IS NOT NULL DROP TABLE dbo.MPMS_MEETING;
IF OBJECT_ID('dbo.MPMS_TASK_STATUS_LOG', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK_STATUS_LOG;
IF OBJECT_ID('dbo.MPMS_TASK_REVIEW_ATTACHMENT', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK_REVIEW_ATTACHMENT;
IF OBJECT_ID('dbo.MPMS_TASK_REVIEW', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK_REVIEW;
IF OBJECT_ID('dbo.MPMS_TASK_ATTACHMENT', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK_ATTACHMENT;
IF OBJECT_ID('dbo.MPMS_TASK_ASSIST', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK_ASSIST;
IF OBJECT_ID('dbo.MPMS_TASK', 'U') IS NOT NULL DROP TABLE dbo.MPMS_TASK;
IF OBJECT_ID('dbo.MPMS_MILESTONE', 'U') IS NOT NULL DROP TABLE dbo.MPMS_MILESTONE;
IF OBJECT_ID('dbo.MPMS_PROJECT', 'U') IS NOT NULL DROP TABLE dbo.MPMS_PROJECT;
IF OBJECT_ID('dbo.MPMS_USER', 'U') IS NOT NULL DROP TABLE dbo.MPMS_USER;
IF OBJECT_ID('dbo.MPMS_ROLE', 'U') IS NOT NULL DROP TABLE dbo.MPMS_ROLE;
GO

-- 3. Create Tables

-- MPMS_ROLE: 角色主檔
CREATE TABLE dbo.MPMS_ROLE (
    role_id INT IDENTITY(1,1) PRIMARY KEY,
    role_code VARCHAR(30) NOT NULL UNIQUE,
    role_name NVARCHAR(50) NOT NULL,
    is_active BIT NOT NULL DEFAULT 1
);

-- MPMS_USER: 使用者主檔
CREATE TABLE dbo.MPMS_USER (
    user_id INT IDENTITY(1,1) PRIMARY KEY,
    account VARCHAR(50) NOT NULL UNIQUE,
    password_hash NVARCHAR(255) NOT NULL,
    user_name NVARCHAR(100) NOT NULL,
    email NVARCHAR(255) NOT NULL,
    role_id INT NOT NULL,
    is_active BIT NOT NULL DEFAULT 1,
    last_login_at DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_USER_ROLE FOREIGN KEY (role_id) REFERENCES dbo.MPMS_ROLE(role_id)
);

-- MPMS_PROJECT: 專案主檔
CREATE TABLE dbo.MPMS_PROJECT (
    project_id INT IDENTITY(1,1) PRIMARY KEY,
    project_code VARCHAR(50) NOT NULL UNIQUE,
    project_name NVARCHAR(200) NOT NULL,
    project_desc NVARCHAR(MAX) NULL,
    pm_user_id INT NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    manual_progress_pct DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    progress_note NVARCHAR(500) NULL,
    project_status VARCHAR(30) NOT NULL DEFAULT 'Planning', -- Planning, Active, Closed, Hold
    is_active BIT NOT NULL DEFAULT 1,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_PROJECT_PM FOREIGN KEY (pm_user_id) REFERENCES dbo.MPMS_USER(user_id),
    CONSTRAINT CK_PROJECT_DATES CHECK (start_date <= end_date),
    CONSTRAINT CK_PROJECT_PROGRESS CHECK (manual_progress_pct >= 0.00 AND manual_progress_pct <= 100.00)
);

-- MPMS_MILESTONE: 里程碑主檔
CREATE TABLE dbo.MPMS_MILESTONE (
    milestone_id INT IDENTITY(1,1) PRIMARY KEY,
    project_id INT NOT NULL,
    milestone_name NVARCHAR(200) NOT NULL,
    target_date DATE NOT NULL,
    sort_no INT NOT NULL DEFAULT 0,
    manual_progress_pct DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    is_completed BIT NOT NULL DEFAULT 0,
    completed_at DATETIME2 NULL,
    remark NVARCHAR(500) NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_MILESTONE_PROJECT FOREIGN KEY (project_id) REFERENCES dbo.MPMS_PROJECT(project_id),
    CONSTRAINT CK_MILESTONE_PROGRESS CHECK (manual_progress_pct >= 0.00 AND manual_progress_pct <= 100.00)
);

-- MPMS_TASK: 任務主檔
CREATE TABLE dbo.MPMS_TASK (
    task_id INT IDENTITY(1,1) PRIMARY KEY,
    milestone_id INT NOT NULL,
    task_title NVARCHAR(200) NOT NULL,
    task_desc NVARCHAR(MAX) NULL,
    owner_user_id INT NOT NULL,
    due_date DATE NOT NULL,
    task_status VARCHAR(30) NOT NULL DEFAULT 'Todo', -- Todo, Progress, Reviewing, Done, Blocked
    priority VARCHAR(20) NOT NULL DEFAULT 'Normal', -- Low, Normal, High
    blocked_reason NVARCHAR(1000) NULL,
    complete_summary NVARCHAR(MAX) NULL,
    review_requested_at DATETIME2 NULL,
    done_at DATETIME2 NULL,
    created_by INT NOT NULL,
    updated_by INT NOT NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    updated_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_TASK_MILESTONE FOREIGN KEY (milestone_id) REFERENCES dbo.MPMS_MILESTONE(milestone_id),
    CONSTRAINT FK_MPMS_TASK_OWNER FOREIGN KEY (owner_user_id) REFERENCES dbo.MPMS_USER(user_id),
    CONSTRAINT FK_MPMS_TASK_CREATOR FOREIGN KEY (created_by) REFERENCES dbo.MPMS_USER(user_id),
    CONSTRAINT FK_MPMS_TASK_UPDATER FOREIGN KEY (updated_by) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_TASK_ASSIST: 任務協辦人
CREATE TABLE dbo.MPMS_TASK_ASSIST (
    task_assist_id INT IDENTITY(1,1) PRIMARY KEY,
    task_id INT NOT NULL,
    assist_user_id INT NOT NULL,
    is_reviewer_candidate BIT NOT NULL DEFAULT 1,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_TASK_ASSIST_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_TASK_ASSIST_USER FOREIGN KEY (assist_user_id) REFERENCES dbo.MPMS_USER(user_id),
    CONSTRAINT UQ_TASK_ASSIST UNIQUE (task_id, assist_user_id)
);

-- MPMS_TASK_ATTACHMENT: 任務附件
CREATE TABLE dbo.MPMS_TASK_ATTACHMENT (
    attachment_id INT IDENTITY(1,1) PRIMARY KEY,
    task_id INT NOT NULL,
    blob_container VARCHAR(100) NOT NULL,
    blob_path NVARCHAR(500) NOT NULL,
    original_file_name NVARCHAR(255) NOT NULL,
    stored_file_name NVARCHAR(255) NOT NULL,
    file_ext VARCHAR(20) NOT NULL,
    file_size_bytes BIGINT NOT NULL,
    content_type VARCHAR(100) NULL,
    summary_text NVARCHAR(MAX) NOT NULL,
    upload_user_id INT NOT NULL,
    uploaded_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    is_void BIT NOT NULL DEFAULT 0,
    void_reason NVARCHAR(500) NULL,
    CONSTRAINT FK_MPMS_TASK_ATTACHMENT_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id),
    CONSTRAINT FK_MPMS_TASK_ATTACHMENT_USER FOREIGN KEY (upload_user_id) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_TASK_REVIEW: 任務審查紀錄
CREATE TABLE dbo.MPMS_TASK_REVIEW (
    review_id INT IDENTITY(1,1) PRIMARY KEY,
    task_id INT NOT NULL,
    reviewer_user_id INT NOT NULL,
    review_result VARCHAR(30) NOT NULL, -- Approved, Rejected
    review_comment NVARCHAR(MAX) NOT NULL,
    reviewed_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    source_type VARCHAR(30) NOT NULL DEFAULT 'PeerReview', -- PeerReview, MeetingReturn, AdminOverride
    CONSTRAINT FK_MPMS_TASK_REVIEW_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id),
    CONSTRAINT FK_MPMS_TASK_REVIEW_USER FOREIGN KEY (reviewer_user_id) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_TASK_REVIEW_ATTACHMENT: 審查與附件關聯表
CREATE TABLE dbo.MPMS_TASK_REVIEW_ATTACHMENT (
    review_attachment_id INT IDENTITY(1,1) PRIMARY KEY,
    review_id INT NOT NULL,
    attachment_id INT NOT NULL,
    CONSTRAINT FK_MPMS_TRA_REVIEW FOREIGN KEY (review_id) REFERENCES dbo.MPMS_TASK_REVIEW(review_id),
    CONSTRAINT FK_MPMS_TRA_ATTACHMENT FOREIGN KEY (attachment_id) REFERENCES dbo.MPMS_TASK_ATTACHMENT(attachment_id)
);

-- MPMS_TASK_STATUS_LOG: 任務狀態異動紀錄
CREATE TABLE dbo.MPMS_TASK_STATUS_LOG (
    status_log_id INT IDENTITY(1,1) PRIMARY KEY,
    task_id INT NOT NULL,
    old_status VARCHAR(30) NULL,
    new_status VARCHAR(30) NOT NULL,
    change_reason NVARCHAR(1000) NULL,
    changed_by INT NOT NULL,
    changed_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_TSL_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_TSL_USER FOREIGN KEY (changed_by) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_MEETING: 週會主檔
CREATE TABLE dbo.MPMS_MEETING (
    meeting_id INT IDENTITY(1,1) PRIMARY KEY,
    meeting_week VARCHAR(10) NOT NULL, -- YYYY-WW
    meeting_date DATE NOT NULL,
    project_id INT NULL, -- NULL 代表全案週會
    host_user_id INT NOT NULL,
    meeting_note NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_MEETING_PROJECT FOREIGN KEY (project_id) REFERENCES dbo.MPMS_PROJECT(project_id),
    CONSTRAINT FK_MPMS_MEETING_HOST FOREIGN KEY (host_user_id) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_MEETING_ACTION: 週會臨時任務
CREATE TABLE dbo.MPMS_MEETING_ACTION (
    action_id INT IDENTITY(1,1) PRIMARY KEY,
    meeting_id INT NOT NULL,
    task_id INT NULL, -- 若已轉為正式任務，則關聯
    action_title NVARCHAR(200) NOT NULL,
    owner_user_id INT NOT NULL,
    due_date DATE NOT NULL,
    action_note NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_ACTION_MEETING FOREIGN KEY (meeting_id) REFERENCES dbo.MPMS_MEETING(meeting_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_ACTION_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id),
    CONSTRAINT FK_MPMS_ACTION_OWNER FOREIGN KEY (owner_user_id) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_WEEKLY_SNAPSHOT: 週快照主檔
CREATE TABLE dbo.MPMS_WEEKLY_SNAPSHOT (
    snapshot_id INT IDENTITY(1,1) PRIMARY KEY,
    snapshot_week VARCHAR(10) NOT NULL, -- YYYY-WW
    meeting_id INT NULL,
    snapshot_version INT NOT NULL DEFAULT 1,
    is_revision BIT NOT NULL DEFAULT 0,
    revision_reason NVARCHAR(1000) NULL,
    sealed_by INT NOT NULL,
    sealed_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_SNAPSHOT_MEETING FOREIGN KEY (meeting_id) REFERENCES dbo.MPMS_MEETING(meeting_id),
    CONSTRAINT FK_MPMS_SNAPSHOT_USER FOREIGN KEY (sealed_by) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_SNAPSHOT_PROJECT: 快照專案資料
CREATE TABLE dbo.MPMS_SNAPSHOT_PROJECT (
    snapshot_project_id INT IDENTITY(1,1) PRIMARY KEY,
    snapshot_id INT NOT NULL,
    project_id INT NOT NULL,
    project_status VARCHAR(30) NOT NULL,
    manual_progress_pct DECIMAL(5,2) NOT NULL,
    progress_note NVARCHAR(500) NULL,
    CONSTRAINT FK_MPMS_SP_SNAPSHOT FOREIGN KEY (snapshot_id) REFERENCES dbo.MPMS_WEEKLY_SNAPSHOT(snapshot_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_SP_PROJECT FOREIGN KEY (project_id) REFERENCES dbo.MPMS_PROJECT(project_id)
);

-- MPMS_SNAPSHOT_MILESTONE: 快照里程碑資料
CREATE TABLE dbo.MPMS_SNAPSHOT_MILESTONE (
    snapshot_milestone_id INT IDENTITY(1,1) PRIMARY KEY,
    snapshot_id INT NOT NULL,
    milestone_id INT NOT NULL,
    manual_progress_pct DECIMAL(5,2) NOT NULL,
    is_completed BIT NOT NULL,
    CONSTRAINT FK_MPMS_SM_SNAPSHOT FOREIGN KEY (snapshot_id) REFERENCES dbo.MPMS_WEEKLY_SNAPSHOT(snapshot_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_SM_MILESTONE FOREIGN KEY (milestone_id) REFERENCES dbo.MPMS_MILESTONE(milestone_id)
);

-- MPMS_SNAPSHOT_TASK: 快照任務資料
CREATE TABLE dbo.MPMS_SNAPSHOT_TASK (
    snapshot_task_id INT IDENTITY(1,1) PRIMARY KEY,
    snapshot_id INT NOT NULL,
    task_id INT NOT NULL,
    task_status VARCHAR(30) NOT NULL,
    owner_user_id INT NOT NULL,
    due_date DATE NOT NULL,
    attachment_count INT NOT NULL,
    latest_review_result VARCHAR(30) NULL,
    blocked_reason NVARCHAR(1000) NULL,
    CONSTRAINT FK_MPMS_ST_SNAPSHOT FOREIGN KEY (snapshot_id) REFERENCES dbo.MPMS_WEEKLY_SNAPSHOT(snapshot_id) ON DELETE CASCADE,
    CONSTRAINT FK_MPMS_ST_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id)
);

-- MPMS_NOTIFICATION: 站內通知
CREATE TABLE dbo.MPMS_NOTIFICATION (
    notification_id INT IDENTITY(1,1) PRIMARY KEY,
    receiver_user_id INT NOT NULL,
    event_type VARCHAR(50) NOT NULL,
    title NVARCHAR(200) NOT NULL,
    message NVARCHAR(MAX) NOT NULL,
    ref_type VARCHAR(50) NULL,
    ref_id INT NULL,
    is_read BIT NOT NULL DEFAULT 0,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_NOTIFICATION_USER FOREIGN KEY (receiver_user_id) REFERENCES dbo.MPMS_USER(user_id)
);

-- MPMS_EMAIL_LOG: Email 發送紀錄
CREATE TABLE dbo.MPMS_EMAIL_LOG (
    email_log_id INT IDENTITY(1,1) PRIMARY KEY,
    receiver_email NVARCHAR(255) NOT NULL,
    subject NVARCHAR(300) NOT NULL,
    body NVARCHAR(MAX) NOT NULL,
    send_status VARCHAR(30) NOT NULL DEFAULT 'Pending', -- Pending, Sent, Failed
    retry_count INT NOT NULL DEFAULT 0,
    error_message NVARCHAR(MAX) NULL,
    sent_at DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE()
);

-- MPMS_AUDIT_LOG: 系統稽核紀錄
CREATE TABLE dbo.MPMS_AUDIT_LOG (
    audit_id BIGINT IDENTITY(1,1) PRIMARY KEY,
    actor_user_id INT NOT NULL,
    action_type VARCHAR(50) NOT NULL,
    target_table VARCHAR(100) NOT NULL,
    target_id VARCHAR(100) NOT NULL,
    old_value_json NVARCHAR(MAX) NULL,
    new_value_json NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_MPMS_AUDIT_USER FOREIGN KEY (actor_user_id) REFERENCES dbo.MPMS_USER(user_id)
);
GO

-- 4. Insert Seed Data
-- 4.1 Insert Roles
INSERT INTO dbo.MPMS_ROLE (role_code, role_name, is_active) VALUES 
('ADMIN', N'系統管理員', 1),
('PM', N'專案經理', 1),
('EMPLOYEE', N'一般人員', 1);
GO

-- 4.2 Insert Users
-- Seed Passwords are 'Password123' (SHA256 representation for reference: we'll use a mocked hash for now)
-- Let's put a simple SHA256 string: 'AQAAAAEAACcQAAAAEGl23m4WjGv/hS5U/x2n7R...'
-- For simplicity, since it's Dapper and V0.1, we will hash it properly in the application,
-- but let's insert standard records.
INSERT INTO dbo.MPMS_USER (account, password_hash, user_name, email, role_id, is_active) VALUES 
('admin', '$2a$11$mVcYQHVH2ILJQNXGUQanDOBdp3.tMdBo0/zsg65YwkHjkXsF9yA0S', N'管理員大明', 'admin@mpms.com', 1, 1),
('pm1', '$2a$11$mVcYQHVH2ILJQNXGUQanDOBdp3.tMdBo0/zsg65YwkHjkXsF9yA0S', N'專案經理小莉', 'pm1@mpms.com', 2, 1),
('pm2', '$2a$11$mVcYQHVH2ILJQNXGUQanDOBdp3.tMdBo0/zsg65YwkHjkXsF9yA0S', N'專案經理小剛', 'pm2@mpms.com', 2, 1),
('emp1', '$2a$11$mVcYQHVH2ILJQNXGUQanDOBdp3.tMdBo0/zsg65YwkHjkXsF9yA0S', N'工程師阿華', 'emp1@mpms.com', 3, 1),
('emp2', '$2a$11$mVcYQHVH2ILJQNXGUQanDOBdp3.tMdBo0/zsg65YwkHjkXsF9yA0S', N'工程師阿強', 'emp2@mpms.com', 3, 1);
GO
