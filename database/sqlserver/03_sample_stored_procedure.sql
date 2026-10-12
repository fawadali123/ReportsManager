-- =====================================================================
-- Reports Management System - sample stored procedure report (SQL Server)
--
-- Optional. Shows how a report can call a procedure instead of running a
-- SELECT. On SQL Server the procedure simply SELECTs; the first result set
-- it returns becomes the report. (There is no @RMS_CURSOR argument here;
-- that is only needed for Oracle.)
--
-- Run after 02_seed_data.sql. Needs SQL Server 2016 SP1 or later for
-- CREATE OR ALTER; on older versions use CREATE PROCEDURE.
-- =====================================================================

USE RMS;
GO

-- Number of runs, exports and failures per report in a date range.
CREATE OR ALTER PROCEDURE dbo.usp_RmsRunsByReport
    @From DATE,
    @To   DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT l.REPORT_NAME,
           COUNT(*)                                                  AS RUNS,
           SUM(CASE WHEN l.ACTION LIKE N'Export%' THEN 1 ELSE 0 END)  AS EXPORTS,
           SUM(CASE WHEN l.SUCCESS = 0 THEN 1 ELSE 0 END)             AS FAILURES,
           AVG(l.DURATION_MS)                                        AS AVERAGE_MS,
           MAX(l.RUN_AT)                                             AS LAST_RUN
      FROM dbo.RMS_REPORT_RUN_LOG l
     WHERE l.RUN_AT >= @From
       AND l.RUN_AT <  DATEADD(DAY, 1, @To)
     GROUP BY l.REPORT_NAME
     ORDER BY COUNT(*) DESC, l.REPORT_NAME;
END
GO

-- The report definition. Note the type 'StoredProcedure' and the call syntax:
-- @From and @To are the procedure's own argument names, @FromDate and @ToDate
-- are the report parameters the user fills in.
INSERT INTO dbo.RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES (N'USAGE_SUMMARY', N'Usage summary', N'Runs, exports and failures per report (stored procedure example)',
        (SELECT ID FROM dbo.RMS_APPLICATIONS WHERE CODE = N'RMS'),
        (SELECT ID FROM dbo.RMS_REPORT_CATEGORIES WHERE CODE = N'USAGE'),
        N'StoredProcedure', 1,
        N'EXEC dbo.usp_RmsRunsByReport @From = @FromDate, @To = @ToDate');

DECLARE @Report INT = SCOPE_IDENTITY();

INSERT INTO dbo.RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES (@Report, N'FromDate', N'From', N'Date', 1, 0, N'START_OF_MONTH', NULL, NULL, 10),
       (@Report, N'ToDate', N'To', N'Date', 1, 0, N'TODAY', NULL, NULL, 20);
GO
