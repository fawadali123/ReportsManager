-- =====================================================================
-- Reports Management System - read-only account for reports (SQL Server)
--
-- Optional, recommended for production. Report SQL is written by
-- administrators and runs with whatever the report connection is allowed
-- to do, so give report connections an account that can only read.
--
-- This script creates the rms_reports_ro login, lets it read every table
-- in the RMS database and run procedures there. Do the same in each of
-- your own application databases that reports should query.
--
-- Run as a sysadmin, after the other scripts. Change the password first.
-- Then add a second connection string and make it the default for reports:
--   "ConnectionStrings": { "ReportsRo": "Server=db-host;Database=RMS;User Id=rms_reports_ro;Password=...;Encrypt=True;TrustServerCertificate=True" }
--   "Reporting":         { "DefaultConnectionName": "ReportsRo" }
-- =====================================================================

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'rms_reports_ro')
BEGIN
    CREATE LOGIN rms_reports_ro WITH PASSWORD = N'Change_This_Password_2!', CHECK_POLICY = ON;
END
GO

USE RMS;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rms_reports_ro')
BEGIN
    CREATE USER rms_reports_ro FOR LOGIN rms_reports_ro;
END
GO

ALTER ROLE db_datareader ADD MEMBER rms_reports_ro;

-- Needed only for stored procedure reports. A procedure runs with its owner's rights,
-- so grant EXECUTE on the specific reporting procedures rather than on the whole schema
-- if any procedure in this database changes data.
IF OBJECT_ID(N'dbo.usp_RmsRunsByReport', N'P') IS NOT NULL   -- created by 03_sample_stored_procedure.sql
BEGIN
    GRANT EXECUTE ON OBJECT::dbo.usp_RmsRunsByReport TO rms_reports_ro;
END
GO
