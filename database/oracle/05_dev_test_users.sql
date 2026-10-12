-- =====================================================================
-- Reports Management System - test users for a development machine (Oracle)
--
-- DEVELOPMENT ONLY. For running the application on a machine that is not
-- on a domain. When Authentication:Mode is "Development" (the setting in
-- appsettings.Development.json), passwords are checked against the fixed
-- accounts in that file instead of Active Directory:
--
--     admin  / admin123     pretends to be in RMS_Admin and RMS_Users
--     viewer / viewer123    pretends to be in RMS_Users
--
-- The account must still exist in RMS_USERS, which is what this script
-- adds. Run after 02_seed_data.sql, as the RMS schema owner. Safe to run
-- more than once. Do not run it on a production database.
-- =====================================================================

-- If the seeded administrator still has the placeholder name, give it to "admin".
UPDATE RMS_USERS
   SET AD_USERNAME = 'admin', FULL_NAME = 'Dev Administrator'
 WHERE CODE = 'ADMIN01'
   AND AD_USERNAME = 'your.ad.username';

INSERT INTO RMS_USERS (CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE)
SELECT 'DEVADMIN', 'Dev Administrator', 'admin@example.com', 'admin', 1, 'Admin'
  FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM RMS_USERS WHERE UPPER(AD_USERNAME) = 'ADMIN');

INSERT INTO RMS_USERS (CODE, FULL_NAME, EMAIL, AD_USERNAME, ACTIVE, ROLE)
SELECT 'DEVVIEWER', 'Dev Viewer', 'viewer@example.com', 'viewer', 1, 'Viewer'
  FROM DUAL
 WHERE NOT EXISTS (SELECT 1 FROM RMS_USERS WHERE UPPER(AD_USERNAME) = 'VIEWER');

-- Give the viewer two of the sample reports, so you can see that the others stay hidden.
INSERT INTO RMS_USER_REPORTS (USER_ID, REPORT_ID, ASSIGNED_BY)
SELECT u.ID, r.ID, 'dev script'
  FROM RMS_USERS u
 CROSS JOIN RMS_REPORTS r
 WHERE UPPER(u.AD_USERNAME) = 'VIEWER'
   AND r.CODE IN ('RUN_HISTORY', 'MY_ACTIVITY')
   AND NOT EXISTS (SELECT 1 FROM RMS_USER_REPORTS x WHERE x.USER_ID = u.ID AND x.REPORT_ID = r.ID);

COMMIT;
