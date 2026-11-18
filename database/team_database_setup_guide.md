# Team local database setup (PostgreSQL 16)

Each developer owns an independent local `woodflow_db`. Install PostgreSQL 16
with pgAdmin, .NET 10 SDK, Node.js 22 and npm 10 or newer. Choose your own private
PostgreSQL password; never put credentials in source control or shared messages.

## New, empty database only

From the repository root in PowerShell (adjust the executable path if needed):

```powershell
$pgUser = Read-Host 'Local PostgreSQL administrator username'
& "$env:ProgramFiles\PostgreSQL\16\bin\createdb.exe" -h localhost -p 5432 -U $pgUser -W woodflow_db
if ($LASTEXITCODE -ne 0) { throw 'Database creation failed; stop and inspect before proceeding.' }
& "$env:ProgramFiles\PostgreSQL\16\bin\psql.exe" -h localhost -p 5432 -U $pgUser -W -d woodflow_db -v ON_ERROR_STOP=1 -f database/schema.sql
if ($LASTEXITCODE -ne 0) { throw 'Schema initialization failed; do not start the application yet.' }
```

Enter your password privately at each prompt. Alternatively create an EMPTY
`woodflow_db` in pgAdmin and execute the entire `schema.sql` there once.
The consolidated schema includes migrations 001–007, required system rows and
sample data. Do NOT apply 001–007 afterward. Standard PostgreSQL 16 supplies
`uuid-ossp`; initialization needs permission to install this extension.
Demo password hashes cannot log in. Initialize your own OWNER using
[AUTH_SETUP.md](../backend/AUTH_SETUP.md); never share an account or setup code.

## Existing databases

Back up first. Never rerun the full schema or reset a working database.
Apply only outstanding files from `database/migrations/`, in numeric order,
with the backend stopped and `psql -v ON_ERROR_STOP=1 -f <migration-path>`
against the intended database (or execute each complete file in pgAdmin).
There is no automatic migration runner/history table: keep an explicit record
of applied migrations. If unsure, inspect the database before applying anything.
Future changes need explicit upgrade migrations; do not tell teammates to paste
arbitrary updated schema sections into an existing database.

## Optional Docker database

Use either native PostgreSQL or Docker on port 5432, not both simultaneously.
From the root, copy the non-secret template and privately edit the ignored file:

```powershell
Copy-Item .env.example .env
notepad .env
docker compose up -d
```

Set `POSTGRES_PASSWORD` to your own unique local password before startup.
Docker binds only `127.0.0.1:5432`, creates `woodflow_db` with user `postgres`,
and initializes the schema ONLY when its named `postgres_data` volume is empty.
Existing volumes/data are preserved. Changing `.env` does NOT change an existing
volume's PostgreSQL password. Do not delete volumes to fix a connection problem.
If you previously used the shared credential, rotate it through a deliberate
administrator password change and update your private local configuration.
Never commit `.env`, database exports, private certificates or password hashes.

## Application connection and HTTPS

Use [backend/README.md](../backend/README.md) to enter credentials privately into
`ConnectionStrings__WoodFlowDb` for your current PowerShell session.
Use [frontend/README.md](../frontend/README.md) to generate and trust your OWN
localhost HTTPS certificate and start Angular. Defaults: API
`https://localhost:7001`, frontend `https://localhost:4200`, PostgreSQL
`localhost:5432`. Credentials and certificates are never supplied by Git.
