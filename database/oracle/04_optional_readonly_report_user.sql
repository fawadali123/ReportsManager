-- =====================================================================
-- Reports Management System - read-only account for reports (Oracle)
--
-- Optional, recommended for production. Report SQL is written by
-- administrators and runs with whatever the report connection is allowed
-- to do, so give report connections an account that can only read.
--
-- This script creates RMS_REPORTS_RO, lets it read the RMS_* tables and
-- run the sample package, and adds synonyms so report SQL can keep using
-- unqualified names. Do the same for your own application schemas.
--
-- Run as a DBA, after the other scripts. Change the password first.
-- Then add a second connection string and make it the default for reports:
--   "ConnectionStrings": { "ReportsRo": "User Id=RMS_REPORTS_RO;Password=...;Data Source=..." }
--   "Reporting":         { "DefaultConnectionName": "ReportsRo" }
-- =====================================================================

CREATE USER RMS_REPORTS_RO IDENTIFIED BY "Change_This_Password_2";

GRANT CREATE SESSION TO RMS_REPORTS_RO;

GRANT SELECT ON RMS.RMS_USERS             TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_APPLICATIONS      TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_REPORT_CATEGORIES TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_REPORTS           TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_REPORT_PARAMETERS TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_USER_REPORTS      TO RMS_REPORTS_RO;
GRANT SELECT ON RMS.RMS_REPORT_RUN_LOG    TO RMS_REPORTS_RO;

CREATE SYNONYM RMS_REPORTS_RO.RMS_USERS             FOR RMS.RMS_USERS;
CREATE SYNONYM RMS_REPORTS_RO.RMS_APPLICATIONS      FOR RMS.RMS_APPLICATIONS;
CREATE SYNONYM RMS_REPORTS_RO.RMS_REPORT_CATEGORIES FOR RMS.RMS_REPORT_CATEGORIES;
CREATE SYNONYM RMS_REPORTS_RO.RMS_REPORTS           FOR RMS.RMS_REPORTS;
CREATE SYNONYM RMS_REPORTS_RO.RMS_REPORT_PARAMETERS FOR RMS.RMS_REPORT_PARAMETERS;
CREATE SYNONYM RMS_REPORTS_RO.RMS_USER_REPORTS      FOR RMS.RMS_USER_REPORTS;
CREATE SYNONYM RMS_REPORTS_RO.RMS_REPORT_RUN_LOG    FOR RMS.RMS_REPORT_RUN_LOG;

-- Only if 03_sample_stored_procedure.sql was run:
GRANT EXECUTE ON RMS.PKG_RMS_REPORTS TO RMS_REPORTS_RO;
CREATE SYNONYM RMS_REPORTS_RO.PKG_RMS_REPORTS FOR RMS.PKG_RMS_REPORTS;
