-- =====================================================================
-- Reports Management System - remove every RMS_* object (SQL Server)
--
-- DESTRUCTIVE: deletes all users, report definitions, assignments and history.
-- Only for resetting a development or test database.
-- =====================================================================

USE RMS;
GO

DROP PROCEDURE IF EXISTS dbo.usp_RmsRunsByReport;

DROP TABLE IF EXISTS dbo.RMS_REPORT_RUN_LOG;
DROP TABLE IF EXISTS dbo.RMS_USER_REPORTS;
DROP TABLE IF EXISTS dbo.RMS_REPORT_PARAMETERS;
DROP TABLE IF EXISTS dbo.RMS_REPORTS;
DROP TABLE IF EXISTS dbo.RMS_REPORT_CATEGORIES;
DROP TABLE IF EXISTS dbo.RMS_APPLICATIONS;
DROP TABLE IF EXISTS dbo.RMS_USERS;
GO
