-- =====================================================================
-- Reports Management System - schema owner (Oracle)
--
-- Step 1 of 3. Creates the RMS user that owns the RMS_* tables and that
-- the application signs in with (ConnectionStrings:RmsDb).
--
-- Run as a DBA (SYSTEM, or a user with CREATE USER), connected to the
-- pluggable database the application will use, not to the CDB root.
--
-- BEFORE RUNNING: change the password, and the tablespace names if yours
-- are different. Skip this script if you already have a schema to use.
-- =====================================================================

CREATE USER RMS IDENTIFIED BY "Change_This_Password_1"
    DEFAULT TABLESPACE USERS
    TEMPORARY TABLESPACE TEMP
    QUOTA UNLIMITED ON USERS;

-- What the owner needs to create the objects in the next scripts.
GRANT CREATE SESSION   TO RMS;
GRANT CREATE TABLE     TO RMS;
GRANT CREATE SEQUENCE  TO RMS;   -- identity columns
GRANT CREATE VIEW      TO RMS;
GRANT CREATE PROCEDURE TO RMS;   -- the sample stored procedure report

-- Connection string for appsettings (replace host, port and service name):
--   "RmsDb": "User Id=RMS;Password=Change_This_Password_1;Data Source=//db-host:1521/ORCLPDB1"
