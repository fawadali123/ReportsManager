-- =====================================================================
-- Reports Management System - starter data (Oracle)
--
-- Step 3 of 3.
-- 1. Creates the first administrator. CHANGE THE VALUES BELOW to your own
--    AD account before running: nobody can sign in until one Admin row exists.
--    That AD account must also be a member of the RMS_Admin group.
-- 2. Creates one application, two categories and four sample reports that
--    query the RMS_* tables themselves, so they work on any installation and
--    show each kind of parameter. Delete them once you have real reports.
--
-- Run after 01_create_tables.sql, as the same schema owner. Run once.
-- =====================================================================

-- ---------------------------------------------------------------------
-- First administrator  (edit these four values)
-- ---------------------------------------------------------------------
INSERT INTO RMS_USERS (CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE)
VALUES ('ADMIN01', 'First Administrator', 'admin@example.com', 'your.ad.username', 1, 'Admin');

-- ---------------------------------------------------------------------
-- Sample application and categories
-- ---------------------------------------------------------------------
INSERT INTO RMS_APPLICATIONS (CODE, NAME, DESCRIPTION, ACTIVE, CONNECTION_NAME)
VALUES ('RMS', 'Reports Management', 'Reports about this system itself', 1, NULL);

INSERT INTO RMS_REPORT_CATEGORIES (CODE, NAME, DESCRIPTION, ACTIVE)
VALUES ('ADMIN', 'Administration', 'Users, access and configuration', 1);

INSERT INTO RMS_REPORT_CATEGORIES (CODE, NAME, DESCRIPTION, ACTIVE)
VALUES ('USAGE', 'Usage', 'How reports are being used', 1);

-- ---------------------------------------------------------------------
-- Sample report 1: no parameters
-- ---------------------------------------------------------------------
INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES ('USER_LIST', 'User list', 'Everyone who can sign in, with role and status',
        (SELECT ID FROM RMS_APPLICATIONS WHERE CODE = 'RMS'),
        (SELECT ID FROM RMS_REPORT_CATEGORIES WHERE CODE = 'ADMIN'),
        'Select', 1,
'SELECT u.CODE,
       u.FULL_NAME,
       u.EMAIL,
       u.AD_USERNAME,
       u.ROLE,
       CASE u.ACTIVE WHEN 1 THEN ''Yes'' ELSE ''No'' END AS ACTIVE,
       (SELECT COUNT(*) FROM RMS_USER_REPORTS ur WHERE ur.USER_ID = u.ID) AS ASSIGNED_REPORTS
  FROM RMS_USERS u
 ORDER BY u.CODE');

-- ---------------------------------------------------------------------
-- Sample report 2: optional parameters - a dropdown fed by a query,
-- a dropdown with a fixed list, and free text
-- ---------------------------------------------------------------------
INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES ('REPORT_CATALOG', 'Report catalogue', 'All report definitions, filtered by application, status or name',
        (SELECT ID FROM RMS_APPLICATIONS WHERE CODE = 'RMS'),
        (SELECT ID FROM RMS_REPORT_CATEGORIES WHERE CODE = 'ADMIN'),
        'Select', 1,
'SELECT a.NAME AS APPLICATION,
       c.NAME AS CATEGORY,
       r.CODE,
       r.NAME,
       r.DESCRIPTION,
       CASE r.ACTIVE WHEN 1 THEN ''Active'' ELSE ''Inactive'' END AS STATUS
  FROM RMS_REPORTS r
  JOIN RMS_APPLICATIONS a ON a.ID = r.APPLICATION_ID
  JOIN RMS_REPORT_CATEGORIES c ON c.ID = r.REPORTS_CATEGORY_ID
 WHERE (@ApplicationId IS NULL OR r.APPLICATION_ID = @ApplicationId)
   AND (@Status IS NULL OR r.ACTIVE = @Status)
   AND (@NameContains IS NULL OR UPPER(r.NAME) LIKE ''%'' || UPPER(@NameContains) || ''%'')
 ORDER BY a.NAME, c.NAME, r.NAME');

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'REPORT_CATALOG'), 'ApplicationId', 'Application', 'Integer', 0, 0, NULL,
        'SELECT ID, NAME FROM RMS_APPLICATIONS ORDER BY NAME', 'Leave empty for all applications', 10);

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'REPORT_CATALOG'), 'Status', 'Status', 'Integer', 0, 0, '1',
        '1=Active;0=Inactive', NULL, 20);

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'REPORT_CATALOG'), 'NameContains', 'Name contains', 'Text', 0, 0, NULL,
        NULL, 'Any part of the report name', 30);

-- ---------------------------------------------------------------------
-- Sample report 3: required date range with moving defaults,
-- and an optional multi-select written as IN (@Users)
-- ---------------------------------------------------------------------
INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES ('RUN_HISTORY', 'Report run history', 'Every report run and export in a date range',
        (SELECT ID FROM RMS_APPLICATIONS WHERE CODE = 'RMS'),
        (SELECT ID FROM RMS_REPORT_CATEGORIES WHERE CODE = 'USAGE'),
        'Select', 1,
'SELECT CAST(l.RUN_AT AS DATE) AS RUN_AT,
       l.USERNAME,
       l.REPORT_NAME,
       l.ACTION,
       l.ROW_COUNT,
       l.DURATION_MS,
       CASE l.SUCCESS WHEN 1 THEN ''Yes'' ELSE ''No'' END AS SUCCESS
  FROM RMS_REPORT_RUN_LOG l
 WHERE l.RUN_AT >= @FromDate
   AND l.RUN_AT <  @ToDate + 1
   AND (@Users IS NULL OR l.USER_ID IN (@Users))
 ORDER BY l.RUN_AT DESC');

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'RUN_HISTORY'), 'FromDate', 'From', 'Date', 1, 0, 'START_OF_MONTH', NULL, NULL, 10);

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'RUN_HISTORY'), 'ToDate', 'To', 'Date', 1, 0, 'TODAY', NULL, NULL, 20);

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'RUN_HISTORY'), 'Users', 'Users', 'Integer', 0, 1, NULL,
        'SELECT ID, NVL(FULL_NAME, AD_USERNAME) FROM RMS_USERS ORDER BY 2', 'Leave empty for everyone', 30);

-- ---------------------------------------------------------------------
-- Sample report 4: a system parameter. @RMS_USERNAME is filled in by the
-- application with the signed-in account, so each person sees only their own rows.
-- ---------------------------------------------------------------------
INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES ('MY_ACTIVITY', 'My recent activity', 'The reports you ran in the last N days',
        (SELECT ID FROM RMS_APPLICATIONS WHERE CODE = 'RMS'),
        (SELECT ID FROM RMS_REPORT_CATEGORIES WHERE CODE = 'USAGE'),
        'Select', 1,
'SELECT CAST(l.RUN_AT AS DATE) AS RUN_AT,
       l.REPORT_NAME,
       l.ACTION,
       l.ROW_COUNT,
       l.DURATION_MS
  FROM RMS_REPORT_RUN_LOG l
 WHERE UPPER(l.USERNAME) = UPPER(@RMS_USERNAME)
   AND l.RUN_AT >= TRUNC(SYSDATE) - @Days
 ORDER BY l.RUN_AT DESC');

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'MY_ACTIVITY'), 'Days', 'Days back', 'Integer', 1, 0, '30', NULL, NULL, 10);

-- ---------------------------------------------------------------------
-- Give the first administrator's account every sample report.
-- (Administrators can run all reports anyway; this matters if the account is later made a Viewer.)
-- ---------------------------------------------------------------------
INSERT INTO RMS_USER_REPORTS (USER_ID, REPORT_ID, ASSIGNED_BY)
SELECT u.ID, r.ID, 'seed'
  FROM RMS_USERS u CROSS JOIN RMS_REPORTS r
 WHERE u.CODE = 'ADMIN01';

COMMIT;
