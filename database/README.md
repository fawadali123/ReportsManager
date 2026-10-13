# Database scripts

One folder per database product. Use the folder that matches where the RMS tables
will live (the `RmsDb` connection). The table and column names are the same in both,
so the application code does not change.

| Step | Oracle (`oracle/`) | SQL Server (`sqlserver/`) | Run as |
|---|---|---|---|
| 1 | `00_create_user.sql` creates the `RMS` schema owner | `00_create_database.sql` creates the `RMS` database and the `rms_app` login | DBA / sysadmin |
| 2 | `01_create_tables.sql` | `01_create_tables.sql` | Oracle: `RMS`. SQL Server: a `db_owner` |
| 3 | `02_seed_data.sql` first administrator and four sample reports | `02_seed_data.sql` | same as step 2 |
| optional | `03_sample_stored_procedure.sql` a report that calls a procedure | `03_sample_stored_procedure.sql` | same as step 2 |
| optional | `04_optional_readonly_report_user.sql` read-only account for report connections | `04_optional_readonly_report_user.sql` | DBA / sysadmin |
| dev only | `05_dev_test_users.sql` adds the `admin` and `viewer` test accounts for a machine with no Active Directory | `05_dev_test_users.sql` | same as step 2 |
| upgrade | `06_upgrade_add_scheduling.sql` adds the schedule tables to a database created before scheduling existed | `06_upgrade_add_scheduling.sql` | same as step 2 |
| reset | `99_drop_all.sql` removes everything | `99_drop_all.sql` | same as step 2 |

Before you run anything:

1. **Change the passwords** in step 1 (and in the optional step 04).
2. **Edit the first `INSERT` in `02_seed_data.sql`** to your own Active Directory account.
   Nobody can sign in until one `Admin` row exists.
3. On SQL Server, if your database is not called `RMS`, change the `USE RMS;` line at the top of each script.

## Running them

Oracle, from the `oracle` folder:

```
sqlplus system@//db-host:1521/ORCLPDB1 @00_create_user.sql
sqlplus RMS@//db-host:1521/ORCLPDB1    @01_create_tables.sql
sqlplus RMS@//db-host:1521/ORCLPDB1    @02_seed_data.sql
```

SQL Server, from the `sqlserver` folder:

```
sqlcmd -S db-host -E -i 00_create_database.sql
sqlcmd -S db-host -E -i 01_create_tables.sql
sqlcmd -S db-host -E -i 02_seed_data.sql
```

SQL Developer and SSMS work just as well: open the file and run it as a script.

## Telling the application which one you used

In `src/RMS.Web/appsettings.Local.json`:

```json
{
  "ConnectionStrings": {
    "RmsDb": "Server=db-host;Database=RMS;User Id=rms_app;Password=...;Encrypt=True;TrustServerCertificate=True"
  },
  "Database": {
    "Provider": "SqlServer"
  }
}
```

For Oracle the provider is `"Oracle"` (the default) and the connection string looks like
`User Id=RMS;Password=...;Data Source=//db-host:1521/ORCLPDB1`.

## Differences between the two versions

| | Oracle | SQL Server |
|---|---|---|
| Generated IDs | identity columns (12c or later) | `IDENTITY(1,1)` |
| Yes/no columns | `NUMBER(1)` with a 0/1 check | `BIT` |
| Long text (`SQL_QUERY`, `LOOKUP_SOURCE`, `PARAMETERS`) | `CLOB` | `NVARCHAR(MAX)` |
| Timestamps | `TIMESTAMP` | `DATETIME2(3)` |
| Case-insensitive unique AD username | function-based index on `UPPER(AD_USERNAME)` | persisted computed column `AD_USERNAME_KEY` |
| Sample report SQL | Oracle SQL | T-SQL |
| Stored procedure report | `PKG.PROC(@A, @RMS_CURSOR)` with a `SYS_REFCURSOR OUT` argument | `EXEC dbo.usp_Proc @A` returning a result set |
