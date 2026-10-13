-- =====================================================================
-- Reports Management System - tables (SQL Server)
-- SQL Server 2016 or later, or Azure SQL Database.
--
-- Step 2 of 3. Run as a member of db_owner in the RMS database
-- (the tables are created in the dbo schema):
--   sqlcmd -S db-host -E -i 01_create_tables.sql
--
-- "User" is a reserved word, so every table carries the RMS_ prefix:
--   User            -> RMS_USERS
--   Application     -> RMS_APPLICATIONS
--   ReportsCategory -> RMS_REPORT_CATEGORIES
--   Reports         -> RMS_REPORTS
-- plus supporting tables:
--   RMS_REPORT_PARAMETERS  optional label/type/dropdown settings per @parameter
--   RMS_USER_REPORTS       which user may run which report
--   RMS_REPORT_RUN_LOG     audit trail of report runs and exports
--   RMS_REPORT_SCHEDULES   reports generated automatically by the RMS Scheduler service
--   RMS_SCHEDULE_RUNS      what each scheduled run produced and where it was delivered
--
-- Table and column names are identical to the Oracle version, so the
-- application code is the same for both databases.
-- =====================================================================

USE RMS;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.RMS_USERS
(
    ID               INT            IDENTITY(1, 1) NOT NULL,
    CODE             NVARCHAR(50)   NOT NULL,
    FULL_NAME        NVARCHAR(200)  NULL,
    EMAIL            NVARCHAR(256)  NULL,
    AD_USERNAME      NVARCHAR(100)  NOT NULL,
    -- AD account names are not case sensitive. Uniqueness is enforced on the upper-cased
    -- value so that it holds whatever the database collation is.
    AD_USERNAME_KEY  AS UPPER(AD_USERNAME) PERSISTED,
    ACTIVE           BIT            NOT NULL CONSTRAINT DF_RMS_USERS_ACTIVE DEFAULT (1),
    ROLE             NVARCHAR(20)   NOT NULL CONSTRAINT DF_RMS_USERS_ROLE DEFAULT (N'Viewer'),
    CREATED_AT       DATETIME2(3)   NOT NULL CONSTRAINT DF_RMS_USERS_CREATED DEFAULT (SYSDATETIME()),
    UPDATED_AT       DATETIME2(3)   NULL,
    CONSTRAINT PK_RMS_USERS PRIMARY KEY (ID),
    CONSTRAINT UK_RMS_USERS_CODE UNIQUE (CODE),
    CONSTRAINT CK_RMS_USERS_ROLE CHECK (ROLE IN (N'Admin', N'Viewer'))
);

CREATE UNIQUE INDEX UK_RMS_USERS_ADUSER ON dbo.RMS_USERS (AD_USERNAME_KEY);
GO


CREATE TABLE dbo.RMS_APPLICATIONS
(
    ID               INT             IDENTITY(1, 1) NOT NULL,
    CODE             NVARCHAR(50)    NOT NULL,
    NAME             NVARCHAR(200)   NOT NULL,
    DESCRIPTION      NVARCHAR(1000)  NULL,
    ACTIVE           BIT             NOT NULL CONSTRAINT DF_RMS_APPLICATIONS_ACTIVE DEFAULT (1),
    -- Name of the connection string its reports run on. NULL = default report connection.
    -- Never the connection string itself.
    CONNECTION_NAME  NVARCHAR(100)   NULL,
    CREATED_AT       DATETIME2(3)    NOT NULL CONSTRAINT DF_RMS_APPLICATIONS_CREATED DEFAULT (SYSDATETIME()),
    UPDATED_AT       DATETIME2(3)    NULL,
    CONSTRAINT PK_RMS_APPLICATIONS PRIMARY KEY (ID),
    CONSTRAINT UK_RMS_APPLICATIONS_CODE UNIQUE (CODE)
);
GO


CREATE TABLE dbo.RMS_REPORT_CATEGORIES
(
    ID           INT             IDENTITY(1, 1) NOT NULL,
    CODE         NVARCHAR(50)    NOT NULL,
    NAME         NVARCHAR(200)   NOT NULL,
    DESCRIPTION  NVARCHAR(1000)  NULL,
    ACTIVE       BIT             NOT NULL CONSTRAINT DF_RMS_REPORT_CAT_ACTIVE DEFAULT (1),
    CREATED_AT   DATETIME2(3)    NOT NULL CONSTRAINT DF_RMS_REPORT_CAT_CREATED DEFAULT (SYSDATETIME()),
    UPDATED_AT   DATETIME2(3)    NULL,
    CONSTRAINT PK_RMS_REPORT_CATEGORIES PRIMARY KEY (ID),
    CONSTRAINT UK_RMS_REPORT_CAT_CODE UNIQUE (CODE)
);
GO


CREATE TABLE dbo.RMS_REPORTS
(
    ID                   INT             IDENTITY(1, 1) NOT NULL,
    CODE                 NVARCHAR(50)    NOT NULL,
    NAME                 NVARCHAR(200)   NOT NULL,
    DESCRIPTION          NVARCHAR(1000)  NULL,
    APPLICATION_ID       INT             NOT NULL,
    REPORTS_CATEGORY_ID  INT             NOT NULL,
    -- SELECT statement (or procedure call) with @Name placeholders for parameters.
    SQL_QUERY            NVARCHAR(MAX)   NOT NULL,
    -- Select = a single SELECT/WITH statement. StoredProcedure = EXEC schema.procedure @Name, ...
    QUERY_TYPE           NVARCHAR(20)    NOT NULL CONSTRAINT DF_RMS_REPORTS_QUERY_TYPE DEFAULT (N'Select'),
    ACTIVE               BIT             NOT NULL CONSTRAINT DF_RMS_REPORTS_ACTIVE DEFAULT (1),
    CREATED_AT           DATETIME2(3)    NOT NULL CONSTRAINT DF_RMS_REPORTS_CREATED DEFAULT (SYSDATETIME()),
    UPDATED_AT           DATETIME2(3)    NULL,
    CONSTRAINT PK_RMS_REPORTS PRIMARY KEY (ID),
    CONSTRAINT UK_RMS_REPORTS_CODE UNIQUE (CODE),
    CONSTRAINT FK_RMS_REPORTS_APPLICATION FOREIGN KEY (APPLICATION_ID) REFERENCES dbo.RMS_APPLICATIONS (ID),
    CONSTRAINT FK_RMS_REPORTS_CATEGORY FOREIGN KEY (REPORTS_CATEGORY_ID) REFERENCES dbo.RMS_REPORT_CATEGORIES (ID),
    CONSTRAINT CK_RMS_REPORTS_QUERY_TYPE CHECK (QUERY_TYPE IN (N'Select', N'StoredProcedure'))
);

CREATE INDEX IX_RMS_REPORTS_APPLICATION ON dbo.RMS_REPORTS (APPLICATION_ID);
CREATE INDEX IX_RMS_REPORTS_CATEGORY ON dbo.RMS_REPORTS (REPORTS_CATEGORY_ID);
GO


-- Optional presentation settings for the @parameters of a report.
-- A parameter with no row is shown as a required text box.
CREATE TABLE dbo.RMS_REPORT_PARAMETERS
(
    ID              INT            IDENTITY(1, 1) NOT NULL,
    REPORT_ID       INT            NOT NULL,
    -- Parameter name as written in SQL_QUERY, without the @.
    NAME            NVARCHAR(100)  NOT NULL,
    NAME_KEY        AS UPPER(NAME) PERSISTED,
    LABEL           NVARCHAR(200)  NOT NULL,
    DATA_TYPE       NVARCHAR(20)   NOT NULL CONSTRAINT DF_RMS_REPORT_PARAMS_TYPE DEFAULT (N'Text'),
    REQUIRED        BIT            NOT NULL CONSTRAINT DF_RMS_REPORT_PARAMS_REQUIRED DEFAULT (1),
    ALLOW_MULTIPLE  BIT            NOT NULL CONSTRAINT DF_RMS_REPORT_PARAMS_MULTI DEFAULT (0),
    -- Literal, or for dates a token: TODAY, START_OF_MONTH, TODAY-7 ...
    DEFAULT_VALUE   NVARCHAR(500)  NULL,
    -- Makes the field a dropdown: a SELECT returning value,label or a fixed list like A=Active;I=Inactive.
    LOOKUP_SOURCE   NVARCHAR(MAX)  NULL,
    HELP_TEXT       NVARCHAR(500)  NULL,
    SORT_ORDER      INT            NOT NULL CONSTRAINT DF_RMS_REPORT_PARAMS_SORT DEFAULT (0),
    CONSTRAINT PK_RMS_REPORT_PARAMETERS PRIMARY KEY (ID),
    CONSTRAINT FK_RMS_REPORT_PARAMS_REPORT FOREIGN KEY (REPORT_ID) REFERENCES dbo.RMS_REPORTS (ID) ON DELETE CASCADE,
    CONSTRAINT CK_RMS_REPORT_PARAMS_TYPE CHECK (DATA_TYPE IN (N'Text', N'Integer', N'Decimal', N'Date', N'DateTime', N'Boolean'))
);

CREATE UNIQUE INDEX UK_RMS_REPORT_PARAMS_NAME ON dbo.RMS_REPORT_PARAMETERS (REPORT_ID, NAME_KEY);
GO


-- Reports each user is allowed to run.
CREATE TABLE dbo.RMS_USER_REPORTS
(
    USER_ID      INT            NOT NULL,
    REPORT_ID    INT            NOT NULL,
    ASSIGNED_AT  DATETIME2(3)   NOT NULL CONSTRAINT DF_RMS_USER_REPORTS_AT DEFAULT (SYSDATETIME()),
    ASSIGNED_BY  NVARCHAR(100)  NULL,
    CONSTRAINT PK_RMS_USER_REPORTS PRIMARY KEY (USER_ID, REPORT_ID),
    CONSTRAINT FK_RMS_USER_REPORTS_USER FOREIGN KEY (USER_ID) REFERENCES dbo.RMS_USERS (ID) ON DELETE CASCADE,
    CONSTRAINT FK_RMS_USER_REPORTS_REPORT FOREIGN KEY (REPORT_ID) REFERENCES dbo.RMS_REPORTS (ID) ON DELETE CASCADE
);

CREATE INDEX IX_RMS_USER_REPORTS_REPORT ON dbo.RMS_USER_REPORTS (REPORT_ID);
GO


-- Who ran or exported which report, when, and with which parameter values.
CREATE TABLE dbo.RMS_REPORT_RUN_LOG
(
    ID             BIGINT          IDENTITY(1, 1) NOT NULL,
    REPORT_ID      INT             NULL,
    -- Name at the time of the run; kept after the report is deleted.
    REPORT_NAME    NVARCHAR(200)   NULL,
    USER_ID        INT             NULL,
    USERNAME       NVARCHAR(100)   NOT NULL,
    RUN_AT         DATETIME2(3)    NOT NULL CONSTRAINT DF_RMS_RUN_LOG_RUN_AT DEFAULT (SYSDATETIME()),
    -- View, Export:xlsx, Export:pdf or Export:csv
    ACTION         NVARCHAR(30)    NOT NULL,
    DURATION_MS    BIGINT          NOT NULL CONSTRAINT DF_RMS_RUN_LOG_DURATION DEFAULT (0),
    ROW_COUNT      INT             NULL,
    SUCCESS        BIT             NOT NULL CONSTRAINT DF_RMS_RUN_LOG_SUCCESS DEFAULT (1),
    ERROR_MESSAGE  NVARCHAR(1000)  NULL,
    PARAMETERS     NVARCHAR(MAX)   NULL,
    CONSTRAINT PK_RMS_REPORT_RUN_LOG PRIMARY KEY (ID),
    CONSTRAINT FK_RMS_RUN_LOG_REPORT FOREIGN KEY (REPORT_ID) REFERENCES dbo.RMS_REPORTS (ID) ON DELETE SET NULL,
    CONSTRAINT FK_RMS_RUN_LOG_USER FOREIGN KEY (USER_ID) REFERENCES dbo.RMS_USERS (ID) ON DELETE SET NULL
);

CREATE INDEX IX_RMS_RUN_LOG_RUN_AT ON dbo.RMS_REPORT_RUN_LOG (RUN_AT);
CREATE INDEX IX_RMS_RUN_LOG_REPORT ON dbo.RMS_REPORT_RUN_LOG (REPORT_ID);
CREATE INDEX IX_RMS_RUN_LOG_USER ON dbo.RMS_REPORT_RUN_LOG (USER_ID);
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
