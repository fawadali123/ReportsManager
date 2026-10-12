# Reports Management System (RMS)

A web application for running parameterised SQL reports against Oracle and SQL Server. Administrators
define reports and decide who may run them; viewers pick a report, fill in its parameters, see the
result in a grid and download it as Excel, PDF or CSV.

- **.NET 8**, ASP.NET Core MVC, Bootstrap 5 (all scripts and styles are bundled; nothing loads from a CDN)
- **Oracle or SQL Server**, chosen per connection, with plain ADO.NET (no ORM). One site can keep its own
  tables in either, and report over both at the same time.
- **Active Directory** sign-in over LDAP, with the `RMS_Admin` and `RMS_Users` groups
- Two roles: **Admin** and **Viewer**

## Contents

1. [Solution layout](#solution-layout)
2. [First-time setup](#first-time-setup)
3. [Oracle, SQL Server, or both](#oracle-sql-server-or-both)
4. [Running without Active Directory](#running-without-active-directory)
5. [Who can sign in, and as what](#who-can-sign-in-and-as-what)
6. [Writing reports](#writing-reports)
7. [Configuration reference](#configuration-reference)
8. [Deploying to IIS](#deploying-to-iis)
9. [Extending the application](#extending-the-application)
10. [Security notes](#security-notes)
11. [What has and has not been verified](#what-has-and-has-not-been-verified)

## Solution layout

```
ReportsManagement.sln
database/              all database scripts; see database/README.md
  oracle/              create user, tables, seed data, sample procedure, read-only account, drop
  sqlserver/           create database, tables, seed data, sample procedure, read-only account, drop
src/
  RMS.Core/            domain model, report engine, business rules. No package dependencies.
    Reporting/         @parameter scanner, SQL translator, validation, value conversion
    Services/          SignInService, ReportService
    Abstractions/      interfaces the other projects implement
  RMS.Infrastructure/  everything that touches the outside world
    Data/              repositories (ADO.NET), written once for both databases
    Data/Providers/    what differs between Oracle and SQL Server, one class each
    ActiveDirectory/   LDAP sign-in and group checks
    Reporting/         runs report queries on whichever connection the report uses
    Export/            Excel (ClosedXML), PDF (QuestPDF), CSV
  RMS.Web/             MVC site: login, Reports screen, Admin area
tests/
  RMS.Tests/           xUnit tests for the engine and the access rules
```

### Tables

`User` is a reserved word in both databases, so the tables carry an `RMS_` prefix. Table and column
names are identical on Oracle and SQL Server.

| You asked for | Table | Added columns |
|---|---|---|
| User | `RMS_USERS` | `FULL_NAME` |
| Application | `RMS_APPLICATIONS` | `CONNECTION_NAME` (which database its reports run on) |
| ReportsCategory | `RMS_REPORT_CATEGORIES` | |
| Reports | `RMS_REPORTS` | `QUERY_TYPE` (`Select` or `StoredProcedure`) |
| | `RMS_USER_REPORTS` | which user may run which report |
| | `RMS_REPORT_PARAMETERS` | optional label / type / dropdown settings per `@parameter` |
| | `RMS_REPORT_RUN_LOG` | audit trail of runs and exports |

## First-time setup

**Prerequisites:** .NET 8 SDK; Oracle Database 12c or later, or SQL Server 2016 or later; and
network access from the web server to a domain controller.

1. **Create the database objects.** The scripts are in `database/oracle` and `database/sqlserver`;
   `database/README.md` lists them in order. In short: `00_` creates the user or database,
   `01_create_tables.sql` the tables, `02_seed_data.sql` the starter data.

2. **Create the first administrator.** Before running `02_seed_data.sql`, change the four values in
   its first `INSERT` to your own AD account. Nobody can sign in until one `Admin` row exists.

3. **Create the AD groups** `RMS_Admin` and `RMS_Users` (or use other names and set them under
   `Access` in the configuration). Put your own account in `RMS_Admin`.

4. **Configure the application.** Create `src/RMS.Web/appsettings.Local.json` (it is git-ignored)
   with your own values. Anything in it overrides `appsettings.json`:

   ```json
   {
     "ConnectionStrings": {
       "RmsDb": "User Id=RMS;Password=...;Data Source=//db-host:1521/ORCLPDB1"
     },
     "Database": {
       "Provider": "Oracle"
     },
     "Authentication": {
       "Mode": "Ldap",
       "Ldap": {
         "Server": "corp.example.com",
         "Domain": "CORP",
         "SearchBase": "DC=corp,DC=example,DC=com"
       }
     }
   }
   ```

5. **Run it.**

   ```
   dotnet restore
   dotnet build
   dotnet run --project src/RMS.Web
   ```

   Or open `ReportsManagement.sln` in Visual Studio and press F5.

6. **Run the tests** (optional): `dotnet test`.

## Oracle, SQL Server, or both

Every database the application talks to is a named entry under `ConnectionStrings`. `RmsDb` holds
the application's own `RMS_*` tables; any other entry is a database reports can run on. The
`Database` section says which product each one is.

**Everything on Oracle** (the default):

```json
"ConnectionStrings": { "RmsDb": "User Id=RMS;Password=...;Data Source=//db-host:1521/ORCLPDB1" },
"Database": { "Provider": "Oracle" }
```

**Everything on SQL Server:**

```json
"ConnectionStrings": { "RmsDb": "Server=db-host;Database=RMS;User Id=rms_app;Password=...;Encrypt=True;TrustServerCertificate=True" },
"Database": { "Provider": "SqlServer" }
```

**Both at once.** `Provider` is the default; `ConnectionProviders` lists the exceptions by connection name:

```json
"ConnectionStrings": {
  "RmsDb":  "User Id=RMS;Password=...;Data Source=//ora-host:1521/ORCLPDB1",
  "ErpDb":  "User Id=ERP_READER;Password=...;Data Source=//ora-host:1521/ERPPDB",
  "CrmDb":  "Server=sql-host;Database=CRM;User Id=crm_reader;Password=...;Encrypt=True;TrustServerCertificate=True"
},
"Database": {
  "Provider": "Oracle",
  "ConnectionProviders": { "CrmDb": "SqlServer" }
}
```

Then, under **Applications**, point each application at its connection. The dropdown shows the
product next to each name, and the report editor tells you which dialect to write for the
application you picked. Nothing else changes: the same screens, the same `@Name` parameters.

Notes for SQL Server:

- `Microsoft.Data.SqlClient` encrypts connections by default. With a self-signed server certificate
  add `TrustServerCertificate=True`, or install a trusted certificate and leave it out.
- For Windows authentication use `Integrated Security=True` and give the IIS application pool
  identity a login (see the comment in `database/sqlserver/00_create_database.sql`).
- An unknown provider name stops the application at start-up with a message naming the setting.

## Running without Active Directory

`appsettings.Development.json` sets `Authentication:Mode` to `Development`. In that mode passwords
are checked against two fixed accounts in the same file instead of AD:

| Username | Password | Pretends to be in |
|---|---|---|
| `admin` | `admin123` | `RMS_Admin`, `RMS_Users` |
| `viewer` | `viewer123` | `RMS_Users` |

The account still has to exist in `RMS_USERS`, so set `AD_USERNAME` to `admin` (and add a `viewer`
row) to use them. The application **refuses to start** in this mode unless
`ASPNETCORE_ENVIRONMENT` is `Development`, so it cannot reach production by accident.

To develop against the real directory instead, set `"Mode": "Ldap"` in `appsettings.Local.json`.

## Who can sign in, and as what

Sign-in checks three things, in this order:

1. The username and password are correct in Active Directory.
2. The account has an **active** row in `RMS_USERS` (matched on `AD_USERNAME`, ignoring case and domain).
3. Group membership, including nested groups, decides the role:

| `ROLE` in RMS_USERS | In `RMS_Admin` | In `RMS_Users` | Result |
|---|---|---|---|
| Admin | yes | any | **Admin** |
| Admin | no | yes | Viewer |
| Viewer | yes | any | Viewer |
| Viewer | no | yes | **Viewer** |
| any | no | no | refused |

So Admin needs both the table and the group. An account that has only one of the two signs in as
a Viewer rather than being locked out. The rule lives in one method,
`SignInService.ResolveRole`, if you want it stricter.

- **Admins** manage users, applications, categories, reports and assignments, and can run every report.
- **Viewers** see only active reports that are assigned to them. This is enforced on the server for
  every request, not just by hiding menu items.
- A signed-in session is re-checked against `RMS_USERS` every two minutes. Deactivating, deleting
  or demoting a user ends their session within that time.

## Writing reports

Reports are defined under **Report setup**. The editor has a "Check SQL and refresh parameters"
button and a built-in help panel; this section is the longer version.

### Parameters

Write `@Name` wherever the user should supply a value:

```sql
SELECT region, SUM(amount) AS total
  FROM sales
 WHERE sale_date >= @FromDate
   AND sale_date <  @ToDate + 1
 GROUP BY region
```

Each distinct name becomes a field on the Reports screen. A query with no `@` runs without asking
for anything. Values are always sent as bind variables, never pasted into the SQL.

Write the SQL in the dialect of the database the report's application points at: Oracle SQL for an
Oracle connection, T-SQL for a SQL Server one. The `@Name` parameters work the same on both; the
application turns them into `:b0` binds on Oracle and `@b0` binds on SQL Server.

An `@` is **not** treated as a parameter when it is inside a string literal, a quoted identifier
(including SQL Server `[brackets]`) or a comment (hints included); when it is glued to a name as in
the Oracle database link `orders@erp_link`; or in SQL Server globals such as `@@ROWCOUNT`.
A trailing `;` or `/`, and T-SQL's leading `;` before `WITH`, are removed automatically.

### Parameter settings (optional)

A parameter with no settings appears as a required text box (names containing "date" start as a
date picker). Per parameter you can set:

| Setting | Effect |
|---|---|
| Label | Text shown above the field. Default: `FromDate` becomes "From Date". |
| Type | Text, whole number, decimal, date, date and time, yes/no. Decides the input control and the bind type. |
| Required | Untick to allow an empty field, which arrives as `NULL`. |
| Default value | A literal, or for dates a token (below). |
| Choices | Turns the field into a dropdown. Either a query returning *value, label* or a fixed list `A=Active;I=Inactive`. |
| Allow several values | Multi-select. Needs choices. |
| Hint | Small text under the field. |
| Order | Use the arrows to arrange the fields. |

**Optional filter**

```sql
AND (@Region IS NULL OR region = @Region)
```

**Multi-select** — write the parameter alone inside parentheses; it expands to one bind per value:

```sql
AND branch_id IN (@Branches)
-- optional version:
AND (@Branches IS NULL OR branch_id IN (@Branches))
```

**Date default tokens** — `TODAY`, `NOW`, `YESTERDAY`, `TOMORROW`, `START_OF_MONTH`, `END_OF_MONTH`,
`START_OF_PREV_MONTH`, `END_OF_PREV_MONTH`, `START_OF_YEAR`, `END_OF_YEAR`, each optionally followed
by `+N` or `-N` days (`TODAY-7`).

### System parameters

These are filled in by the application and never shown on the form. Use them to limit a report, or
a dropdown's choices, to the person running it:

| Parameter | Value |
|---|---|
| `@RMS_USERNAME` | AD account name of the signed-in user |
| `@RMS_USER_ID` | `RMS_USERS.ID` |
| `@RMS_USER_CODE` | `RMS_USERS.CODE` |
| `@RMS_USER_EMAIL` | `RMS_USERS.EMAIL` |

Every name starting with `RMS_` is reserved. A dropdown only accepts the values its choices query
returned for that user, so a per-user choices query is a real restriction, not just a convenience.

### Stored procedures

For logic that does not fit in one SELECT, set the type to **Stored procedure** and write only the call.

**Oracle:** pass `@RMS_CURSOR` for the procedure's `SYS_REFCURSOR OUT` argument. Named notation
(`p_from => @FromDate`) works too.

```sql
pkg_sales.by_region(@FromDate, @ToDate, @RMS_CURSOR)
```

**SQL Server:** the procedure just SELECTs, and its first result set becomes the report. Arguments
can be positional or named; in `@From = @FromDate` the left side is the procedure's own argument
name and the right side is the report parameter.

```sql
EXEC dbo.usp_SalesByRegion @FromDate, @ToDate
EXEC dbo.usp_SalesByRegion @From = @FromDate, @To = @ToDate
```

`database/*/03_sample_stored_procedure.sql` has a working example for each. Set
`Reporting:AllowStoredProcedures` to `false` to permit SELECT reports only.

### Reports against other databases

Each application can run its reports on its own database:

1. Add a named connection string on the server, for example `"ErpDb": "User Id=RPT_READER;..."`.
   If it is a different product than `Database:Provider`, add it to `Database:ConnectionProviders`.
2. In **Applications**, choose that name under "Database connection".

Only the *name* is stored in the database. Applications with no connection use
`Reporting:DefaultConnectionName`.

### Limits

The on-screen grid shows the first `MaxPreviewRows` rows (default 5,000) and says so when there are
more. Excel and CSV exports go up to `MaxExportRows` (100,000), PDF up to `MaxPdfRows` (10,000).
A query is cancelled after `CommandTimeoutSeconds` (120).

## Configuration reference

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:RmsDb` | | Database holding the `RMS_*` tables. Required. |
| `ConnectionStrings:<name>` | | Any further entry is a database that reports can target. |
| `Database:Provider` | `Oracle` | `Oracle` or `SqlServer`. Applies to every connection not listed below. |
| `Database:ConnectionProviders:<name>` | | Product of one connection, when it differs from the default. |
| `Authentication:Mode` | `Ldap` | `Ldap` or `Development` (see above). |
| `Authentication:SessionMinutes` | 60 | Idle time before a session expires. |
| `Authentication:Ldap:Server` | | Domain controller, or the AD DNS domain name. Comma-separate several. |
| `Authentication:Ldap:Port` | 0 | 0 = 389, or 636 with `UseSsl`. |
| `Authentication:Ldap:UseSsl` | false | Use LDAPS. |
| `Authentication:Ldap:AuthType` | `Negotiate` | `Negotiate` (Kerberos/NTLM, for Windows servers) or `Basic` (simple bind; requires `UseSsl`). |
| `Authentication:Ldap:Domain` | | NetBIOS domain, e.g. `CORP`. |
| `Authentication:Ldap:UpnSuffix` | | For `Basic`: bind as `user@suffix`. |
| `Authentication:Ldap:SearchBase` | domain root | Where users and groups are searched. |
| `Access:AdminGroup` / `Access:UsersGroup` | `RMS_Admin` / `RMS_Users` | AD group names. |
| `Reporting:*` | | Row limits, timeout, default connection, stored procedure switch. |
| `Export:Pdf:License` | `Community` | QuestPDF licence tier (see security notes). |
| `Export:Pdf:FontFamily` | | e.g. `Segoe UI, Tahoma` for Arabic text in PDFs. |

Every setting can also come from an environment variable, with `__` for `:`
(`ConnectionStrings__RmsDb`), or from `dotnet user-secrets`.

## Deploying to IIS

1. Install the **ASP.NET Core 8 Hosting Bundle** on the server.
2. `dotnet publish src/RMS.Web -c Release -o C:\inetpub\rms`
3. Create an IIS site pointing at that folder, application pool "No Managed Code".
4. Set **Load User Profile = True** on the application pool. Otherwise the keys that protect the
   sign-in cookie are regenerated on every restart and everyone is signed out.
5. Put the production settings in environment variables or an `appsettings.Production.json` beside
   the published files, and make sure `ASPNETCORE_ENVIRONMENT` is not `Development`.
6. Bind the site to HTTPS. The sign-in form posts a password.

## Extending the application

| To add | Do this |
|---|---|
| Another export format | Implement `IReportExporter`, register it in `RMS.Infrastructure/DependencyInjection.cs`. A button appears on the Reports screen. |
| Another parameter type | Add to `ParameterDataType`, handle it in `ParameterValueConverter` (parse), `OracleReportExecutor.ToParameter` (bind) and `reports.js` (input control). |
| Another system parameter | Add it to `SystemParameters`. |
| Another kind of report | Add to `ReportQueryType` and teach `ReportSqlValidator` and `ReportSqlTranslator` about it. |
| Another database product | Implement `IDatabaseProvider` (see `OracleProvider` and `SqlServerProvider`), add it to `DatabaseKind` and `DatabaseProviders`, and teach `SqlScanner` its quoting rules if they differ. |
| Another identity source | Implement `IDirectoryAuthenticator`. |
| Stricter or looser sign-in rules | `SignInService.ResolveRole`. |

Registrations use `TryAdd`, so a service registered before `AddReportsManagement(...)` in
`Program.cs` replaces the built-in one.

## Security notes

- **Use a read-only database account for report connections.** Report SQL is restricted to a
  single `SELECT`/`WITH` statement (or a procedure call), but that check guards against mistakes.
  The account's privileges are what actually limits what a report can touch. Ideally `RmsDb` and
  the report connections are different accounts; `04_optional_readonly_report_user.sql` in each
  script folder sets one up.
- **Report SQL is trusted input.** Anyone with the Admin role can read whatever the report
  connections can read.
- Viewers never see raw database error text, only the error code. Admins see the full message.
- All POSTs require an antiforgery token; the login form is rate-limited to 10 attempts per minute
  per address; an empty password is rejected before it reaches LDAP (where it would count as an
  anonymous bind).
- With `AuthType: Basic` the password is sent to the domain controller as-is, so the application
  refuses to do that without `UseSsl`.
- **QuestPDF licence.** PDF export uses QuestPDF. Its Community licence is free only for
  organisations below a revenue threshold; check https://www.questpdf.com/license/ before going
  live. It was chosen because it shapes Arabic text correctly, which the MIT-licensed alternatives
  do not. The exporter is one class behind `IReportExporter` if you need to replace it.

## What has and has not been verified

**Verified**

- The first version (Oracle only) was built on the development machine in Visual Studio against the
  real NuGet packages: `RMS.Core`, `RMS.Infrastructure` and `RMS.Web` all compiled.
- This version compiles with zero warnings against those same real assemblies for ODP.NET,
  System.DirectoryServices.Protocols, ClosedXML and QuestPDF.
- The 173 unit tests pass: parameter scanner, SQL translation and validation for both dialects,
  value conversion, sign-in rules, report access rules.
- Every screen was exercised in a real browser against the real controllers, views, scripts and
  business rules, with in-memory data standing in for the database: login and logout, role checks,
  cascading pickers, generated parameter forms, validation, grid sort/filter/paging, all Admin
  screens, assignments, session end after deactivation, antiforgery rejection, mobile layout, and an
  Oracle connection and a SQL Server connection side by side.
- Excel export was run with the real ClosedXML library and the workbook opened and inspected
  (typed cells, date formats, frozen header, filter, Arabic text). CSV likewise.

**Not verified. Please check on your side**

- **`Microsoft.Data.SqlClient`** is new in this version and could not be downloaded where this was
  written, so the SQL Server provider was type-checked against a hand-written stand-in of the few
  calls it makes. The first `dotnet build` is the real test; a mismatch would be a small fix in
  `SqlServerProvider.cs`.
- **Everything that talks to a database**, on either product: the scripts in `database/`, the
  repositories' SQL, and report execution (including both stored procedure paths) have never run
  against a real Oracle or SQL Server instance.
- **LDAP sign-in** has never run against a domain controller.
- **PDF output** has never been generated: QuestPDF's native rendering library could not be loaded
  where this was written. If it fails to load on a server, only the PDF button is affected.
- The **xUnit test project** has only been run with a stand-in test runner, not `dotnet test`.
