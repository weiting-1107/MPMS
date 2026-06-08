USE MPMS;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. 建立全新 dbo.MPMS_PROJECT_PHASE (專案階段表，取代原 Milestone)
IF OBJECT_ID('dbo.MPMS_PROJECT_PHASE', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MPMS_PROJECT_PHASE (
        phase_id INT IDENTITY(1,1) PRIMARY KEY,
        project_id INT NOT NULL,
        phase_name NVARCHAR(200) NOT NULL,
        start_date DATE NOT NULL,
        end_date DATE NOT NULL,
        sort_no INT NOT NULL DEFAULT 0,
        manual_progress_pct DECIMAL(5,2) NOT NULL DEFAULT 0.00,
        phase_status VARCHAR(30) NOT NULL DEFAULT 'Planned', -- Planned, Active, Completed, Voided
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        updated_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_MPMS_PROJECT_PHASE_PROJECT FOREIGN KEY (project_id) REFERENCES dbo.MPMS_PROJECT(project_id),
        CONSTRAINT CK_PROJECT_PHASE_PROGRESS CHECK (manual_progress_pct >= 0.00 AND manual_progress_pct <= 100.00),
        CONSTRAINT CK_PROJECT_PHASE_DATES CHECK (start_date <= end_date)
    );
END;
GO

-- 2. 進行資料遷移：將現有 MPMS_MILESTONE 資料複製到 MPMS_PROJECT_PHASE
-- 同步保留 identity ID，讓 MPMS_TASK 的關聯 ID 在更名後仍然可以對齊
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'MPMS_MILESTONE') 
   AND EXISTS (SELECT * FROM sys.tables WHERE name = 'MPMS_PROJECT_PHASE')
   AND NOT EXISTS (SELECT 1 FROM dbo.MPMS_PROJECT_PHASE)
BEGIN
    SET IDENTITY_INSERT dbo.MPMS_PROJECT_PHASE ON;
    
    INSERT INTO dbo.MPMS_PROJECT_PHASE (
        phase_id, 
        project_id, 
        phase_name, 
        start_date, 
        end_date, 
        sort_no, 
        manual_progress_pct, 
        phase_status, 
        created_at, 
        updated_at
    )
    SELECT 
        m.milestone_id, 
        m.project_id, 
        m.milestone_name, 
        -- start_date = max(project.start_date, target_date - 30天)
        CASE 
            WHEN DATEADD(day, -30, m.target_date) < p.start_date THEN p.start_date 
            ELSE DATEADD(day, -30, m.target_date) 
        END,
        -- end_date = min(project.end_date, target_date)
        CASE 
            WHEN m.target_date > p.end_date THEN p.end_date 
            ELSE m.target_date 
        END,
        m.sort_no, 
        m.manual_progress_pct, 
        CASE WHEN m.is_completed = 1 THEN 'Completed' ELSE 'Active' END,
        m.created_at, 
        m.updated_at
    FROM dbo.MPMS_MILESTONE m
    INNER JOIN dbo.MPMS_PROJECT p ON m.project_id = p.project_id;
    
    SET IDENTITY_INSERT dbo.MPMS_PROJECT_PHASE OFF;
END;
GO

-- 3. 調整 dbo.MPMS_TASK 表
-- 3.1 移除舊有外鍵 FK_MPMS_TASK_MILESTONE
IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_MPMS_TASK_MILESTONE')
BEGIN
    ALTER TABLE dbo.MPMS_TASK DROP CONSTRAINT FK_MPMS_TASK_MILESTONE;
END;
GO

-- 3.2 重新命名 milestone_id 為 phase_id
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_TASK') AND name = 'milestone_id')
BEGIN
    EXEC sp_rename 'dbo.MPMS_TASK.milestone_id', 'phase_id', 'COLUMN';
END;
GO

-- 3.3 建立指向 MPMS_PROJECT_PHASE 的新外鍵
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_MPMS_TASK_PHASE')
BEGIN
    ALTER TABLE dbo.MPMS_TASK ADD CONSTRAINT FK_MPMS_TASK_PHASE 
    FOREIGN KEY (phase_id) REFERENCES dbo.MPMS_PROJECT_PHASE(phase_id);
END;
GO

-- 3.4 新增甘特圖與計畫起訖相關欄位至 MPMS_TASK
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_TASK') AND name = 'planned_start_date')
BEGIN
    ALTER TABLE dbo.MPMS_TASK ADD planned_start_date DATE NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_TASK') AND name = 'is_milestone')
BEGIN
    ALTER TABLE dbo.MPMS_TASK ADD is_milestone BIT NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_TASK') AND name = 'gantt_sort_no')
BEGIN
    ALTER TABLE dbo.MPMS_TASK ADD gantt_sort_no INT NOT NULL DEFAULT 0;
END;
GO

-- 3.5 補齊舊有資料的 planned_start_date 預設值 (設為截止日的前 7 天)
UPDATE dbo.MPMS_TASK
SET planned_start_date = DATEADD(day, -7, due_date)
WHERE planned_start_date IS NULL;
GO

-- 4. 建立相依關係表 dbo.MPMS_TASK_DEPENDENCY
IF OBJECT_ID('dbo.MPMS_TASK_DEPENDENCY', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MPMS_TASK_DEPENDENCY (
        dependency_id INT IDENTITY(1,1) PRIMARY KEY,
        task_id INT NOT NULL,
        predecessor_task_id INT NOT NULL,
        dependency_type VARCHAR(20) NOT NULL DEFAULT 'FS', -- Finish-to-Start
        created_at DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_MPMS_TD_TASK FOREIGN KEY (task_id) REFERENCES dbo.MPMS_TASK(task_id),
        CONSTRAINT FK_MPMS_TD_PREDECESSOR FOREIGN KEY (predecessor_task_id) REFERENCES dbo.MPMS_TASK(task_id),
        CONSTRAINT UQ_TASK_DEPENDENCY UNIQUE (task_id, predecessor_task_id),
        CONSTRAINT CK_NO_SELF_DEPENDENCY CHECK (task_id <> predecessor_task_id)
    );
END;
GO

-- 5. 重構週快照子表
-- 5.1 移除舊快照子表 MPMS_SNAPSHOT_MILESTONE
IF OBJECT_ID('dbo.MPMS_SNAPSHOT_MILESTONE', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.MPMS_SNAPSHOT_MILESTONE;
END;
GO

-- 5.2 建立全新快照階段表 MPMS_SNAPSHOT_PHASE
IF OBJECT_ID('dbo.MPMS_SNAPSHOT_PHASE', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MPMS_SNAPSHOT_PHASE (
        snapshot_phase_id INT IDENTITY(1,1) PRIMARY KEY,
        snapshot_id INT NOT NULL,
        phase_id INT NOT NULL,
        manual_progress_pct DECIMAL(5,2) NOT NULL,
        phase_status VARCHAR(30) NOT NULL,
        CONSTRAINT FK_MPMS_SPH_SNAPSHOT FOREIGN KEY (snapshot_id) REFERENCES dbo.MPMS_WEEKLY_SNAPSHOT(snapshot_id) ON DELETE CASCADE,
        CONSTRAINT FK_MPMS_SPH_PHASE FOREIGN KEY (phase_id) REFERENCES dbo.MPMS_PROJECT_PHASE(phase_id)
    );
END;
GO

-- 5.3 調整 MPMS_SNAPSHOT_TASK，新增甘特圖欄位與前置關係摘要
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_SNAPSHOT_TASK') AND name = 'planned_start_date')
BEGIN
    ALTER TABLE dbo.MPMS_SNAPSHOT_TASK ADD planned_start_date DATE NULL;
END;
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MPMS_SNAPSHOT_TASK') AND name = 'predecessor_summary')
BEGIN
    ALTER TABLE dbo.MPMS_SNAPSHOT_TASK ADD predecessor_summary NVARCHAR(1000) NULL;
END;
GO

-- 6. 安全清理舊的 MPMS_MILESTONE 表
IF OBJECT_ID('dbo.MPMS_MILESTONE', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.MPMS_MILESTONE;
END;
GO
