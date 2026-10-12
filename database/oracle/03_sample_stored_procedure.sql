-- =====================================================================
-- Reports Management System - sample stored procedure report (Oracle)
--
-- Optional. Shows how a report can call a procedure instead
-- of running a SELECT: the procedure hands its rows back through a
-- SYS_REFCURSOR OUT argument, and the report definition passes
-- @RMS_CURSOR in that position.
--
-- Run after 02_seed_data.sql, as the RMS schema owner.
-- =====================================================================

CREATE OR REPLACE PACKAGE PKG_RMS_REPORTS AS

    -- Number of runs, exports and failures per report in a date range.
    PROCEDURE RUNS_BY_REPORT
    (
        p_from   IN  DATE,
        p_to     IN  DATE,
        p_result OUT SYS_REFCURSOR
    );

END PKG_RMS_REPORTS;
/

CREATE OR REPLACE PACKAGE BODY PKG_RMS_REPORTS AS

    PROCEDURE RUNS_BY_REPORT
    (
        p_from   IN  DATE,
        p_to     IN  DATE,
        p_result OUT SYS_REFCURSOR
    ) IS
    BEGIN
        OPEN p_result FOR
            SELECT l.REPORT_NAME,
                   COUNT(*)                                               AS RUNS,
                   SUM(CASE WHEN l.ACTION LIKE 'Export%' THEN 1 ELSE 0 END) AS EXPORTS,
                   SUM(CASE WHEN l.SUCCESS = 0 THEN 1 ELSE 0 END)           AS FAILURES,
                   ROUND(AVG(l.DURATION_MS))                              AS AVERAGE_MS,
                   CAST(MAX(l.RUN_AT) AS DATE)                            AS LAST_RUN
              FROM RMS_REPORT_RUN_LOG l
             WHERE l.RUN_AT >= p_from
               AND l.RUN_AT <  p_to + 1
             GROUP BY l.REPORT_NAME
             ORDER BY COUNT(*) DESC, l.REPORT_NAME;
    END RUNS_BY_REPORT;

END PKG_RMS_REPORTS;
/

-- The report definition. Note the type 'StoredProcedure' and the call syntax.
INSERT INTO RMS_REPORTS (CODE, NAME, DESCRIPTION, APPLICATION_ID, REPORTS_CATEGORY_ID, QUERY_TYPE, ACTIVE, SQL_QUERY)
VALUES ('USAGE_SUMMARY', 'Usage summary', 'Runs, exports and failures per report (stored procedure example)',
        (SELECT ID FROM RMS_APPLICATIONS WHERE CODE = 'RMS'),
        (SELECT ID FROM RMS_REPORT_CATEGORIES WHERE CODE = 'USAGE'),
        'StoredProcedure', 1,
        'PKG_RMS_REPORTS.RUNS_BY_REPORT(@FromDate, @ToDate, @RMS_CURSOR)');

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'USAGE_SUMMARY'), 'FromDate', 'From', 'Date', 1, 0, 'START_OF_MONTH', NULL, NULL, 10);

INSERT INTO RMS_REPORT_PARAMETERS (REPORT_ID, NAME, LABEL, DATA_TYPE, REQUIRED, ALLOW_MULTIPLE, DEFAULT_VALUE, LOOKUP_SOURCE, HELP_TEXT, SORT_ORDER)
VALUES ((SELECT ID FROM RMS_REPORTS WHERE CODE = 'USAGE_SUMMARY'), 'ToDate', 'To', 'Date', 1, 0, 'TODAY', NULL, NULL, 20);

COMMIT;
