-- =====================================================================
-- Reports Management System - starter data (SQL Server)
--
-- Step 3 of 3.
-- 1. Creates the first administrator. CHANGE THE VALUES BELOW to your own
--    AD account before running: nobody can sign in until one Admin row exists.
--    That AD account must also be a member of the RMS_Admin group.
-- 2. Creates one application, two categories and four sample reports that
--    query the RMS_* tables themselves, so they work on any installation and
--    show each kind of parameter. Delete them once you have real reports.
--
-- Run after 01_create_tables.sql. Run once.
--
-- The sample reports are the same as in the Oracle seed script, written in
-- T-SQL: + instead of ||, DATEADD instead of date arithmetic, ISNULL for NVL.
-- =====================================================================

USE RMS;
GO

SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------
-- First administrator  (edit these four values)
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_USERS (CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE)
VALUES (N'ADMIN01', N'First Administrator', N'admin@example.com', N'your.ad.username', 1, N'Admin');
GO

-- ---------------------------------------------------------------------
-- Sample application and categories
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_APPLICATIONS (CODE, NAME, DESCRIPTION, ACTIVE, CONNECTION_NAME)
VALUES (N'RMS', N'Reports Management', N'Reports about this system itself', 1, NULL);

INSERT INTO dbo.RMS_REPORT_CATEGORIES (CODE, NAME, DESCRIPTION, ACTIVE)
VALUES (N'ADMIN', N'Administration', N'Users, access and configuration', 1),
       (N'USAGE', N'Usage', N'How reports are being used', 1);
GO

DECLARE @Application INT = (SELECT ID FROM dbo.RMS_APPLICATIONS WHERE CODE = N'RMS');
DECLARE @Administration INT = (SELECT ID FROM dbo.RMS_REPORT_CATEGORIES WHERE CODE = N'ADMIN');
DECLARE @Usage INT = (SELECT ID FROM dbo.RMS_REPORT_CATEGORIES WHERE CODE = N'USAGE');
DECLARE @Report INT;

-- ---------------------------------------------------------------------
-- Sample report 1: no parameters
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES (N'USER_LIST', N'User list', N'Everyone who can sign in, with role and status', @Application, @Administration, N'Select', 1,
N'SELECT u.CODE,
       u.FULL_NAME,
       u.EMAIL,
       u.AD_USERNAME,
       u.ROLE,
       CASE u.ACTIVE WHEN 1 THEN ''Yes'' ELSE ''No'' END AS ACTIVE,
       (SELECT COUNT(*) FROM dbo.RMS_USER_REPORTS ur WHERE ur.USER_ID = u.ID) AS ASSIGNED_REPORTS
  FROM dbo.RMS_USERS u
 ORDER BY u.CODE');

-- ---------------------------------------------------------------------
-- Sample report 2: optional parameters - a dropdown fed by a query,
-- a dropdown with a fixed list, and free text
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES (N'REPORT_CATALOG', N'Report catalogue', N'All report definitions, filtered by application, status or name', @Application, @Administration, N'Select', 1,
N'SELECT a.NAME AS APPLICATION,
       c.NAME AS CATEGORY,
       r.CODE,
       r.NAME,
       r.DESCRIPTION,
       CASE r.ACTIVE WHEN 1 THEN ''Active'' ELSE ''Inactive'' END AS STATUS
  FROM dbo.RMS_REPORTS r
  JOIN dbo.RMS_APPLICATIONS a ON a.ID = r.APPLICATION_ID
  JOIN dbo.RMS_REPORT_CATEGORIES c ON c.ID = r.REPORTS_CATEGORY_ID
 WHERE (@ApplicationId IS NULL OR r.APPLICATION_ID = @ApplicationId)
   AND (@Status IS NULL OR r.ACTIVE = @Status)
   AND (@NameContains IS NULL OR r.NAME LIKE ''%'' + @NameContains + ''%'')
 ORDER BY a.NAME, c.NAME, r.NAME');

SET @Report = SCOPE_IDENTITY();

INSERT INTO dbo.RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES (@Report, N'ApplicationId', N'Application', N'Integer', 0, 0, NULL, N'SELECT ID, NAME FROM dbo.RMS_APPLICATIONS ORDER BY NAME', N'Leave empty for all applications', 10),
       (@Report, N'Status', N'Status', N'Integer', 0, 0, N'1', N'1=Active;0=Inactive', NULL, 20),
       (@Report, N'NameContains', N'Name contains', N'Text', 0, 0, NULL, NULL, N'Any part of the report name', 30);

-- ---------------------------------------------------------------------
-- Sample report 3: required date range with moving defaults,
-- and an optional multi-select written as IN (@Users)
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES (N'RUN_HISTORY', N'Report run history', N'Every report run and export in a date range', @Application, @Usage, N'Select', 1,
N'SELECT l.RUN_AT,
       l.USERNAME,
       l.REPORT_NAME,
       l.ACTION,
       l.ROW_COUNT,
       l.DURATION_MS,
       CASE l.SUCCESS WHEN 1 THEN ''Yes'' ELSE ''No'' END AS SUCCESS
  FROM dbo.RMS_REPORT_RUN_LOG l
 WHERE l.RUN_AT >= @FromDate
   AND l.RUN_AT <  DATEADD(DAY, 1, @ToDate)
   AND (@Users IS NULL OR l.USER_ID IN (@Users))
 ORDER BY l.RUN_AT DESC');

SET @Report = SCOPE_IDENTITY();

INSERT INTO dbo.RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES (@Report, N'FromDate', N'From', N'Date', 1, 0, N'START_OF_MONTH', NULL, NULL, 10),
       (@Report, N'ToDate', N'To', N'Date', 1, 0, N'TODAY', NULL, NULL, 20),
       (@Report, N'Users', N'Users', N'Integer', 0, 1, NULL, N'SELECT ID, ISNULL(FULL_NAME, AD_USERNAME) AS NAME FROM dbo.RMS_USERS ORDER BY 2', N'Leave empty for everyone', 30);

-- ---------------------------------------------------------------------
-- Sample report 4: a system parameter. @RMS_USERNAME is filled in by the
-- application with the signed-in account, so each person sees only their own rows.
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES (N'MY_ACTIVITY', N'My recent activity', N'The reports you ran in the last N days', @Application, @Usage, N'Select', 1,
N'SELECT l.RUN_AT,
       l.REPORT_NAME,
       l.ACTION,
       l.ROW_COUNT,
       l.DURATION_MS
  FROM dbo.RMS_REPORT_RUN_LOG l
 WHERE UPPER(l.USERNAME) = UPPER(@RMS_USERNAME)
   AND l.RUN_AT >= DATEADD(DAY, -@Days, CAST(GETDATE() AS DATE))
 ORDER BY l.RUN_AT DESC');

SET @Report = SCOPE_IDENTITY();

INSERT INTO dbo.RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES (@Report, N'Days', N'Days back', N'Integer', 1, 0, N'30', NULL, NULL, 10);

-- ---------------------------------------------------------------------
-- Give the first administrator's account every sample report.
-- (Administrators can run all reports anyway; this matters if the account is later made a Viewer.)
-- ---------------------------------------------------------------------
INSERT INTO dbo.RMS_USER_REPORTS (USER_ID, REPORT_ID, ASSIGNED_BY)
SELECT u.ID, r.ID, N'seed'
  FROM dbo.RMS_USERS u CROSS JOIN dbo.RMS_REPORTS r
 WHERE u.CODE = N'ADMIN01';
GO
