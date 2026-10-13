-- =====================================================================
-- Reports Management System - add report scheduling (SQL Server)
--
-- Only for a database created BEFORE scheduling existed. A new installation
-- gets these tables from 01_create_tables.sql and does not need this script.
--   sqlcmd -S db-host -E -i 06_upgrade_add_scheduling.sql
-- =====================================================================

USE RMS;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.RMS_REPORT_SCHEDULES
(
    ID                 INT             IDENTITY(1, 1) NOT NULL,
    REPORT_ID          INT             NOT NULL,
    NAME               NVARCHAR(200)   NOT NULL,
    ACTIVE             BIT             NOT NULL CONSTRAINT DF_RMS_SCHEDULES_ACTIVE DEFAULT (1),
    -- Once, Hourly, Daily, Weekly or Monthly
    FREQUENCY          NVARCHAR(10)    NOT NULL,
    -- HH:mm in TIME_ZONE. Hourly: first run of the day.
    RUN_TIME           NVARCHAR(5)     NULL,
    -- Hourly only: no runs after this HH:mm.
    END_TIME           NVARCHAR(5)     NULL,
    INTERVAL_HOURS     TINYINT         NULL,
    -- Comma list such as Sun,Mon,Tue. Required for Weekly, optional filter for Hourly and Daily.
    DAYS_OF_WEEK       NVARCHAR(30)    NULL,
    -- Monthly: 1-31, or 0 for the last day of the month.
    DAY_OF_MONTH       TINYINT         NULL,
    -- Once: local date and time in TIME_ZONE.
    RUN_ONCE_AT        DATETIME2(0)    NULL,
    START_DATE         DATE            NULL,
    END_DATE           DATE            NULL,
    TIME_ZONE          NVARCHAR(64)    NOT NULL,
    EXPORT_FORMAT      NVARCHAR(10)    NOT NULL CONSTRAINT DF_RMS_SCHEDULES_FORMAT DEFAULT (N'xlsx'),
    -- JSON object of parameter name to list of values; dates may be tokens such as START_OF_PREV_MONTH.
    PARAMETER_VALUES   NVARCHAR(MAX)   NULL,
    SKIP_IF_EMPTY      BIT             NOT NULL CONSTRAINT DF_RMS_SCHEDULES_SKIP DEFAULT (0),
    SEND_EMAIL         BIT             NOT NULL CONSTRAINT DF_RMS_SCHEDULES_EMAIL DEFAULT (0),
    EMAIL_TO           NVARCHAR(2000)  NULL,
    EMAIL_CC           NVARCHAR(2000)  NULL,
    EMAIL_SUBJECT      NVARCHAR(300)   NULL,
    EMAIL_BODY         NVARCHAR(4000)  NULL,
    SAVE_TO_SHARE      BIT             NOT NULL CONSTRAINT DF_RMS_SCHEDULES_SHARE DEFAULT (0),
    SHARE_FOLDER       NVARCHAR(500)   NULL,
    FILE_NAME_PATTERN  NVARCHAR(200)   NULL,
    -- When the scheduler runs it next, in UTC.
    NEXT_RUN_UTC       DATETIME2(3)    NULL,
    LAST_RUN_UTC       DATETIME2(3)    NULL,
    LAST_STATUS        NVARCHAR(20)    NULL,
    RUN_REQUESTED_UTC  DATETIME2(3)    NULL,
    RUN_REQUESTED_BY   NVARCHAR(100)   NULL,
    -- Scheduler instance running it now; stops two services running the same schedule.
    LOCKED_BY          NVARCHAR(100)   NULL,
    LOCKED_UNTIL_UTC   DATETIME2(3)    NULL,
    CREATED_BY         NVARCHAR(100)   NULL,
    CREATED_AT         DATETIME2(3)    NOT NULL CONSTRAINT DF_RMS_SCHEDULES_CREATED DEFAULT (SYSDATETIME()),
    UPDATED_BY         NVARCHAR(100)   NULL,
    UPDATED_AT         DATETIME2(3)    NULL,
    CONSTRAINT PK_RMS_REPORT_SCHEDULES PRIMARY KEY (ID),
    CONSTRAINT FK_RMS_SCHEDULES_REPORT FOREIGN KEY (REPORT_ID) REFERENCES dbo.RMS_REPORTS (ID) ON DELETE CASCADE,
    CONSTRAINT CK_RMS_SCHEDULES_FREQ CHECK (FREQUENCY IN (N'Once', N'Hourly', N'Daily', N'Weekly', N'Monthly'))
);

CREATE INDEX IX_RMS_SCHEDULES_DUE ON dbo.RMS_REPORT_SCHEDULES (ACTIVE, NEXT_RUN_UTC);
CREATE INDEX IX_RMS_SCHEDULES_REPORT ON dbo.RMS_REPORT_SCHEDULES (REPORT_ID);
GO

CREATE TABLE dbo.RMS_SCHEDULE_RUNS
(
    ID                 BIGINT          IDENTITY(1, 1) NOT NULL,
    -- Null once the schedule is deleted; SCHEDULE_NAME keeps what it was called.
    SCHEDULE_ID        INT             NULL,
    SCHEDULE_NAME      NVARCHAR(200)   NOT NULL,
    -- No foreign key: the report may be deleted later and the history should stay.
    REPORT_ID          INT             NULL,
    REPORT_NAME        NVARCHAR(200)   NULL,
    -- Schedule or Manual (Run now)
    TRIGGER_TYPE       NVARCHAR(10)    NOT NULL,
    SCHEDULED_FOR_UTC  DATETIME2(3)    NULL,
    STARTED_UTC        DATETIME2(3)    NOT NULL,
    FINISHED_UTC       DATETIME2(3)    NULL,
    -- Running, Succeeded, Partial (one delivery failed), Failed or Skipped (no rows)
    STATUS             NVARCHAR(20)    NOT NULL,
    ROW_COUNT          INT             NULL,
    TRUNCATED          BIT             NOT NULL CONSTRAINT DF_RMS_SCHEDULE_RUNS_TRUNC DEFAULT (0),
    FILE_NAME          NVARCHAR(260)   NULL,
    FILE_SIZE          BIGINT          NULL,
    EMAIL_RESULT       NVARCHAR(1000)  NULL,
    SHARE_RESULT       NVARCHAR(1000)  NULL,
    ERROR_MESSAGE      NVARCHAR(2000)  NULL,
    MACHINE_NAME       NVARCHAR(100)   NULL,
    CONSTRAINT PK_RMS_SCHEDULE_RUNS PRIMARY KEY (ID),
    CONSTRAINT FK_RMS_SCHEDULE_RUNS_SCHEDULE FOREIGN KEY (SCHEDULE_ID) REFERENCES dbo.RMS_REPORT_SCHEDULES (ID) ON DELETE SET NULL
);

CREATE INDEX IX_RMS_SCHEDULE_RUNS_SCHEDULE ON dbo.RMS_SCHEDULE_RUNS (SCHEDULE_ID, STARTED_UTC);
CREATE INDEX IX_RMS_SCHEDULE_RUNS_STARTED ON dbo.RMS_SCHEDULE_RUNS (STARTED_UTC);
GO
