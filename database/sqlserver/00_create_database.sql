-- =====================================================================
-- Reports Management System - database and application login (SQL Server)
--
-- Step 1 of 3. Creates the RMS database, and the rms_app login the
-- application signs in with (ConnectionStrings:RmsDb).
--
-- Run as a sysadmin, for example in SSMS or:
--   sqlcmd -S db-host -E -i 00_create_database.sql
--
-- BEFORE RUNNING: change the password. Skip the CREATE DATABASE part if
-- you already have a database to use, and change the USE statement in
-- this and the following scripts to its name.
-- =====================================================================

IF DB_ID(N'RMS') IS NULL
BEGIN
    CREATE DATABASE RMS;
END
GO

-- A SQL login for the web application. To use Windows authentication instead,
-- create the login from the IIS application pool identity:
--   CREATE LOGIN [CORP\svc-rms] FROM WINDOWS;
-- and use "Integrated Security=True" in the connection string.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'rms_app')
BEGIN
    CREATE LOGIN rms_app WITH PASSWORD = N'Change_This_Password_1!', CHECK_POLICY = ON;
END
GO

USE RMS;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rms_app')
BEGIN
    CREATE USER rms_app FOR LOGIN rms_app;
END
GO

-- The application reads and writes the RMS_* tables and runs procedures. It does not
-- need to create or alter anything, so it is not made a database owner.
ALTER ROLE db_datareader ADD MEMBER rms_app;
ALTER ROLE db_datawriter ADD MEMBER rms_app;
GRANT EXECUTE ON SCHEMA::dbo TO rms_app;
GO

-- Connection string for appsettings (replace the server name):
--   "RmsDb": "Server=db-host;Database=RMS;User Id=rms_app;Password=Change_This_Password_1!;Encrypt=True;TrustServerCertificate=True"
-- and tell the application this connection is SQL Server:
--   "Database": { "Provider": "SqlServer" }
