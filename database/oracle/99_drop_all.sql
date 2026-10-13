-- =====================================================================
-- Reports Management System - remove every RMS_* object (Oracle)
--
-- DESTRUCTIVE: deletes all users, report definitions, assignments and history.
-- Only for resetting a development or test schema.
-- =====================================================================

DROP TABLE RMS_SCHEDULE_RUNS     CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_REPORT_SCHEDULES  CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_REPORT_RUN_LOG    CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_USER_REPORTS      CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_REPORT_PARAMETERS CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_REPORTS           CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_REPORT_CATEGORIES CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_APPLICATIONS      CASCADE CONSTRAINTS PURGE;
DROP TABLE RMS_USERS             CASCADE CONSTRAINTS PURGE;

-- Only exists if 03_sample_stored_procedure.sql was run. Ignore the error otherwise.
DROP PACKAGE PKG_RMS_REPORTS;
