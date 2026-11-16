# Local backend

For authentication, HTTPS, manual role migrations and first OWNER initialization,
follow [AUTH_SETUP.md](AUTH_SETUP.md). Use the HTTPS quick start below for local development.

Requires the .NET 10 SDK and local PostgreSQL 16 with the existing
`woodflow_db` database and schema already installed. This backend does not
create databases, tables, or migrations.

From the repository root in PowerShell:

```powershell
dotnet build backend/WoodFlow.slnx
$dbCredential = Get-Credential -Message "Local PostgreSQL credentials"
$dbPassword = $dbCredential.GetNetworkCredential().Password.Replace('"', '""')
$dbUsername = $dbCredential.UserName.Replace('"', '""')
$env:ConnectionStrings__WoodFlowDb = 'Host=localhost;Port=5432;Database=woodflow_db;Username="' + $dbUsername + '";Password="' + $dbPassword + '"'
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet dev-certs https --trust
dotnet run --project backend/WoodFlow.Api --no-launch-profile -- --urls https://localhost:7001
```

Use your actual local PostgreSQL port if it differs. Credentials are supplied
through ASP.NET Core configuration, never committed. An ignored
`WoodFlow.Api/appsettings.Development.json` can also provide
`ConnectionStrings:WoodFlowDb` when running in Development. Environment
variables override JSON configuration. Use each developer's local PostgreSQL 16
database woodflow_db. Connection settings remain configurable for future hosting.

Visit `https://localhost:7001/api/health`, or use
`curl.exe -i https://localhost:7001/api/health`.
A successful connection returns HTTP 200 with
`{"applicationStatus":"running","databaseStatus":"connected"}`.
Missing configuration or an unavailable database returns HTTP 503 with
`databaseStatus: "unavailable"`; no database error details are returned.
The check only opens a connection; it does not execute SQL or verify the schema.
Building and starting without database configuration are supported.

Dependencies: Api -> Application + Infrastructure; Infrastructure -> Application;
Application -> Domain; Domain has no project dependencies. Domain contains User
and centralized role definitions. Repository contracts
belong to Application, implementations and Npgsql access to Infrastructure.
Cookie authentication and first OWNER setup are described in AUTH_SETUP.md.
No other business modules are included.

After stopping the API, clear the shell's connection setting:

```powershell
Remove-Item Env:ConnectionStrings__WoodFlowDb
Remove-Variable dbCredential, dbPassword, dbUsername
```
