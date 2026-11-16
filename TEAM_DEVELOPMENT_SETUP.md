# WoodFlow team development setup

This is the authoritative team setup guide. Use it before older planning notes.
Commands below are for Windows 10/11 PowerShell. Replace text inside `<...>`
with your own value. Do not copy the angle brackets.

**One-time setup:** sections 2–6, dependency restore in section 7, and section 8.1.
**Everyday work:** start your database, configure the backend terminal again,
start both applications in section 7, then follow sections 9–15.

## 1. What WoodFlow is

WoodFlow manages a real furniture shop, its point of sale (POS), and workshop.
It is **not an online shopping platform**. The common application and security
foundation already exists. Each member adds an assigned business module.

| Tool or part | Purpose |
| --- | --- |
| Angular 18 frontend | Shows pages, forms and navigation in the browser. |
| Node.js 22 and npm | Run Angular development tools and install packages. |
| .NET 10 / ASP.NET Core backend | Runs the API, business rules and security checks. |
| PostgreSQL 16 database | Stores accounts and business records on your laptop. |
| Git and GitHub | Track code changes and let the team review them. |
| Visual Studio Code | Edits the project and provides terminals. |

An API is the backend interface that the frontend calls. Each developer uses
their own local database. Your accounts and records stay separate from others.

## 2. Install software first — one time

Install these before cloning. Use the official Windows installers. Restart
PowerShell and VS Code after installation so new commands are found.

| Required software | Official link | Installation choice / version check |
| --- | --- | --- |
| PostgreSQL **16** | [Windows installer](https://www.postgresql.org/download/windows/) | Include server, command-line tools and pgAdmin 4. Keep port 5432. Choose your own private `postgres` password. |
| .NET **10 SDK** | [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Choose the Windows SDK matching your laptop, usually x64. `dotnet --list-sdks` must include `10.0.*`. |
| Node.js **22** | [Node downloads](https://nodejs.org/en/download) | Select version 22, not the site's newest default. Include npm. `node --version` must show `v22.*`; `npm.cmd --version` should be 10 or newer. |
| Angular CLI **18** | [Angular 18 setup](https://v18.angular.dev/tools/cli/setup-local/) | Install the optional global command below. The repository also locks its own CLI, used by npm scripts. |
| Git | [Git for Windows](https://git-scm.com/install/windows) | Allow Git in command prompts. Check `git --version`. |
| VS Code | [VS Code download](https://code.visualstudio.com/download) | Enable “Add to PATH” if offered. Check `code --version`. |

For a global Angular 18 command, run from any PowerShell directory:

```powershell
npm.cmd install --global @angular/cli@18
ng.cmd version
```

PostgreSQL and the .NET SDK are **separate installations**. Installing one does
not install the other. A .NET runtime alone cannot build this project.
Do not create a new Angular app or upgrade dependencies during setup.
Use `npm.cmd` if PowerShell blocks `npm.ps1`; no execution-policy change is needed.

Docker is optional, not required. Chrome or Chromium is needed only for the
optional browser tests. A GitHub account and repository access are needed to clone
and submit work. No paid services or cloud database are needed.

## 3. Clone and open the project — one time

1. Ask the leader for the correct repository URL and access. On GitHub, copy
   **Code → HTTPS**. Do not put an access token in the URL or share it.
2. Open PowerShell in the parent folder where you want the project.
3. Run these commands with your team's URL:

```powershell
git clone <repository-url> woodflow-adbms
cd woodflow-adbms
code .
git branch --show-current
git switch -c feature/<your-name>-<assigned-module>
```

The **repository root** is the folder containing `README.md`, `backend/` and
`frontend/`. Unless a block says otherwise, run commands from this root.
Use a feature branch so unfinished work does not change `main` for everyone.

| Location | Contents |
| --- | --- |
| `backend/WoodFlow.Api/` | Controllers, middleware, startup and API configuration. |
| `backend/WoodFlow.Application/` | Services, interfaces, validation and DTOs (request/response shapes). |
| `backend/WoodFlow.Domain/` | Domain models, role names and permission definitions. |
| `backend/WoodFlow.Infrastructure/` | PostgreSQL repositories and password hashing. |
| `backend/WoodFlow.Tests/` | Backend tests. |
| `frontend/src/app/` | Angular `core/`, `shared/`, `features/` and `layouts/`. |
| `frontend/public/assets/` | Local images, icons and fonts. |
| `frontend/scripts/`, `frontend/tests/` | HTTPS startup and isolated frontend checks. |
| `database/schema.sql` | Complete fresh-database schema through migration 007. |
| `database/migrations/` | Upgrade scripts for existing databases. |
| `docs/` | Business requirements, planning notes and recorded module allocations. |
| `.env.example`, `docker-compose.yml` | Optional local Docker database setup. |

If essential folders are missing after cloning, contact the leader. Do not
recreate the projects. The leader must include their source files in the team commit.

## 4. PostgreSQL setup — one time, NEW database only

A database stores records. The schema file creates its tables, relationships,
functions, triggers and initial records. The consolidated schema was previously
verified against the earlier schema plus migrations 001–007.

**NEVER rerun the consolidated schema on an existing populated database.**
**NEVER reset or overwrite another developer's database.**

### 4.1 Check the server and connect

From the repository root:

```powershell
$pgBin = Join-Path $env:ProgramFiles 'PostgreSQL\16\bin'
& "$pgBin\psql.exe" --version
Get-Service *postgres*
& "$pgBin\pg_isready.exe" -h localhost -p 5432
$pgUser = Read-Host 'Your local PostgreSQL administrator username (usually postgres)'
& "$pgBin\psql.exe" -h localhost -p 5432 -U $pgUser -W -d postgres -c 'SELECT version(), current_database();'
```

Expect PostgreSQL 16, a running service and “accepting connections”. Enter your
local password only at the prompt. If installed elsewhere, change `$pgBin` to
your own PostgreSQL 16 `bin` directory. Do not commit that personal path.
If the service is stopped, open Windows **Services**, find your PostgreSQL 16
service and start it. Administrator permission may be needed.

### 4.2 Create and initialize an EMPTY database

Run only when `woodflow_db` does not already exist. Stop if any command fails.

```powershell
& "$pgBin\createdb.exe" -h localhost -p 5432 -U $pgUser -W woodflow_db
if ($LASTEXITCODE -ne 0) { throw 'Database creation failed. Stop; do not import the schema.' }
& "$pgBin\psql.exe" -h localhost -p 5432 -U $pgUser -W -d woodflow_db -v ON_ERROR_STOP=1 -f database/schema.sql
if ($LASTEXITCODE -ne 0) { throw 'Schema import failed. Stop and ask for help.' }
```

**Do NOT run migrations 001–007 after this import. They are already included.**
The schema includes demonstration records and placeholder account hashes, not a
shared working login. Use first-OWNER setup in section 8.1.

### 4.3 Verify the import

These read-only commands print no passwords, hashes, codes or account records:

```powershell
& "$pgBin\psql.exe" -h localhost -p 5432 -U $pgUser -W -d woodflow_db -v ON_ERROR_STOP=1 -c "SELECT current_database(), version(); SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename;"
& "$pgBin\psql.exe" -h localhost -p 5432 -U $pgUser -W -d woodflow_db -v ON_ERROR_STOP=1 -c "SELECT to_regclass('public.users'), to_regclass('public.owner_setup_state'), to_regclass('public.assistant_permission_settings'), to_regclass('public.assistant_user_permissions'), to_regclass('public.password_recovery_requests'), to_regclass('public.auth_sessions');"
```

The second command must show all six table names, not blank values.
Standard PostgreSQL 16 supplies the required `uuid-ossp` extension. The local
administrator must have permission to install it during import.
The API health check in section 7 confirms the application's connection separately.

**pgAdmin alternative:** connect to your local PostgreSQL 16 server; create an
empty `woodflow_db`; select it; open Query Tool; open `database/schema.sql`;
execute the entire file once; inspect Messages for errors. Run the read-only
queries above in Query Tool if desired. Do not run the schema against `postgres`
or an existing populated `woodflow_db`.

### 4.4 Existing databases and optional Docker

For an existing database, back up first and check which migrations were applied.
There is no automatic migration history table. Coordinate with the leader if
uncertain. With the backend stopped, apply only outstanding numbered migrations,
in order, using the complete files and `ON_ERROR_STOP=1`. Never guess or rerun
schema sections as an upgrade. Preserve migrations 001–007 unchanged.

Docker users can follow [the database guide](database/team_database_setup_guide.md).
Only one PostgreSQL server can use port 5432 at a time. Docker initializes the
schema automatically only on an empty `postgres_data` volume. Do not import it
again. Never delete an existing volume to fix connection errors.

## 5. Private configuration — each backend terminal

The backend reads `ConnectionStrings:WoodFlowDb` through ASP.NET Core configuration.
The environment variable `ConnectionStrings__WoodFlowDb` overrides JSON settings.
An environment variable is a setting passed to a program by its terminal.

From the root, in the terminal that will run the backend:

```powershell
$dbCredential = Get-Credential -Message 'Your local PostgreSQL username and password'
$dbPassword = $dbCredential.GetNetworkCredential().Password.Replace('"', '""')
$dbUsername = $dbCredential.UserName.Replace('"', '""')
$env:ConnectionStrings__WoodFlowDb = 'Host=localhost;Port=5432;Database=woodflow_db;Username="' + $dbUsername + '";Password="' + $dbPassword + '"'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Remove-Variable dbCredential, dbPassword, dbUsername
```

Enter your PostgreSQL credentials, not your WoodFlow login. Do not print the
environment variable or save its value in a shared script. It remains available
to child processes until you clear it or close that terminal. Repeat this block
in a new backend terminal. After stopping the API, you can clear it with:

```powershell
Remove-Item Env:ConnectionStrings__WoodFlowDb
```

An alternative is private `backend/WoodFlow.Api/appsettings.Development.json`,
which is ignored and loaded in Development. Ask for help rather than writing a
secret into tracked `appsettings.json`. Never put secrets in Angular environment
files: browser code is public.

**`.env.example` is for optional Docker only.** ASP.NET Core does not automatically
read `.env`, and this project has no dotenv loader. Docker users may run this once
from the root, then privately fill the blank password in `.env`:

```powershell
Copy-Item .env.example .env
notepad .env
```

Do not overwrite an existing `.env`. Docker still needs backend configuration
above. Changing Docker `.env` does not change a password stored in an existing
database volume. Do not share passwords through GitHub, chat or screenshots.

Ignored files include `.env`, local appsettings, certificate keys, `node_modules`,
`bin`, `obj`, `dist` and `.angular`. `.env.example` is a safe template, not a secret.
Check both ignore rules **and already tracked files** before committing (section 13).
Ignore rules do not remove files already tracked by Git.

## 6. Local HTTPS certificates — one time per laptop/user

HTTPS encrypts browser connections. WoodFlow requires it for authentication and
setup, and uses Secure cookies. Do not replace HTTPS with HTTP.

From the root, after installing .NET 10:

```powershell
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force frontend/.certs | Out-Null
dotnet dev-certs https --export-path frontend/.certs/localhost.pem --format PEM --no-password
dotnet dev-certs https --check --trust
```

Accept the Windows trust prompt for your own development certificate. The export
creates `frontend/.certs/localhost.pem` and `localhost.key`. The key is unencrypted
for the local development server; keep it private. Generate your own files, never
copy a teammate's key or commit either file. Do not re-export over working files
unless you are deliberately renewing/replacing your local certificate.

`npm start` runs `frontend/scripts/serve-https.mjs`. It checks these files and sets
`NODE_EXTRA_CA_CERTS` so Angular trusts the backend certificate. `proxy.conf.json`
sends relative `/api` requests to `https://localhost:7001` with `secure: true`.
The browser sees one HTTPS origin, which supports cookies and CSRF without adding
a credentialed CORS policy.

Normal frontend: **https://localhost:4200**. Backend: **https://localhost:7001**.
Port **4300** is an allowed alternative for existing development/browser tests:
`npm.cmd start -- --port 4300`. Use the corresponding HTTPS URL.

If a certificate is renewed, export the matching new files and restart both
servers. Restart the browser after trusting it. Never use `secure: false`,
`NODE_TLS_REJECT_UNAUTHORIZED=0`, HTTP authentication, or browser certificate
bypasses as fixes. See [Microsoft's certificate reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)
and [the frontend guide](frontend/README.md).

## 7. Restore and run WoodFlow

Start in this order: **PostgreSQL → backend → frontend → browser**.

1. **First restore / after backend package changes:** from the root, with any
   old API stopped using Ctrl+C:

   ```powershell
   dotnet restore backend/WoodFlow.slnx
   dotnet build backend/WoodFlow.slnx --configuration Release --no-restore
   ```

2. **Backend terminal, everyday:** configure section 5 in this terminal. Then:

   ```powershell
   dotnet run --project backend/WoodFlow.Api --configuration Release --no-launch-profile -- --urls https://localhost:7001
   ```

   Wait for “Now listening on: https://localhost:7001”. Starting the API does not
   import SQL, create accounts or generate setup codes.

3. **Check health:** in another root terminal:

   ```powershell
   curl.exe https://localhost:7001/api/health
   ```

   Expect `applicationStatus: running` and `databaseStatus: connected`.
   An unavailable database returns HTTP 503. Health only opens a connection;
   it does not prove that every table exists.

4. **Frontend terminal, first restore / after lockfile changes:** from the root:

   ```powershell
   cd frontend
   npm.cmd ci
   ```

   `npm ci` installs the exact lockfile dependencies. Stop Angular before running
   it; Windows can lock esbuild files. Do not delete or rewrite the lockfile.

5. **Frontend terminal, everyday:** inside `frontend/`:

   ```powershell
   npm.cmd start
   ```

6. Open **https://localhost:4200**. Choose **Start WoodFlow** on the splash page.
   Loading checks your session, then opens login or the dashboard.
   To check the proxy, open `https://localhost:4200/api/health`.

Stop each server with **Ctrl+C in its own terminal**. Do not close another member's
process or delete certificates/database data to restart. After pulling changes,
check whether dependencies or explicit database upgrades are required.

## 8. Features already available

### 8.1 First OWNER setup — once per installation

A new installation does not automatically create an OWNER or expose public
registration. Startup routes unauthenticated visitors to login. Missing cookies
or a new browser do not mean the installation is new.

1. Initialize your own fresh database and build Release as above.
2. In a root terminal configured using section 5, a trusted local administrator runs:

   ```powershell
   dotnet run --project backend/WoodFlow.Api --configuration Release --no-build --no-launch-profile -- --generate-owner-setup-code
   ```

3. The command prints a secret authorization once. Do not log, screenshot or share
   it publicly. Only its hash is saved. It expires after **10 minutes**.
4. Start the applications. Manually open `https://localhost:4200/initial-setup`.
   Enter full name, an unused username, password, confirmation and the code.
   Username: 3–50 letters/digits/`_`/`.`/`-`. Setup password: 12–128 characters.
5. Submit, then log in normally. Setup does not automatically sign you in.

Codes are single-use. Before initialization, generating a replacement invalidates
the previous code. After initialization, setup is sealed. A usable OWNER hash,
even on an inactive account, must not reopen bootstrap. Demo placeholder hashes
cannot log in. Never overwrite seed users or manually reset `owner_setup_state`.
If every OWNER loses access, contact the security owner for a separate trusted
administrator recovery procedure. There is no public bypass.

### 8.2 Login, logout and sessions

Login checks username, password and active status. The browser receives a Secure,
HttpOnly cookie: JavaScript cannot read that authentication cookie. There are no
JWTs or browser-stored passwords. Tabs in one browser profile share cookies;
use separate browser profiles/private windows when testing different users.

Sessions have an eight-hour maximum and no sliding extension. PostgreSQL stores
hashed session handles. Logout revokes the current session on the server; copied
cookies from that session stop working. Other sessions remain valid unless an
account-wide change invalidates them. Expired/revoked sessions require login again.

### 8.3 Users & Roles

Only an active **OWNER** can manage accounts at `/users` and its APIs. OWNER has
fixed administrative access. ASSISTANT has only permitted business access.

OWNER can create users, search/page listings, view details, change roles,
suspend accounts, reactivate suspended accounts and permanently deactivate them.
Deactivation retains history and cannot be reversed through normal workflows.
Unsafe self-changes and removing the last usable active OWNER are blocked.
Security-sensitive role/status changes invalidate existing sessions.
User creation and password recovery currently accept 8–128-character passwords;
first-OWNER setup requires 12–128. Use a long, unique password with mixed characters.

### 8.4 Dynamic permissions and RBAC

RBAC means access based on a user's role. OWNER manages shared ASSISTANT settings
and individual ASSISTANT overrides. An override **replaces** shared grants; it is
not added to them. Resetting it restores inheritance from current role defaults.
The fresh default is only `dashboard.view`. Create/Edit/Delete grants require View.

For example, grant `inventory.view` but leave `inventory.edit` disabled so an
assistant can inspect stock without changing it. Administrative access cannot
be granted through this business permission matrix.

Backend permission checks read current stored grants, including for existing
sessions. Frontend checks guide navigation; they are not the security boundary.
Permission keys for future modules do **not** automatically protect their APIs.

### 8.5 Forgot Password / password recovery

1. On login, choose Forgot Password. Enter username and select an OWNER contact.
   The response is generic even for an unknown/ineligible account.
2. The selected OWNER sees the assigned request count and inbox under Users & Roles.
   Counts refresh on navigation/manual refresh/actions, not continuous polling.
3. The OWNER verifies the person's identity **outside the application**, then denies
   or explicitly approves. An OWNER must use another OWNER for their own recovery.
4. Approval shows a random reset code once. It expires in **10 minutes**. The OWNER
   copies it and gives it privately to the verified person. There is no automatic
   email, SMS or WhatsApp delivery. Closing/leaving clears the one-time display.
5. The requester opens `/reset-password` from the recovery flow, enters the code
   and matching new passwords. Success consumes the code, updates the hash,
   invalidates earlier sessions and returns them to login.

Pending requests expire after **one hour**. Repeated submissions cannot overwrite
an approved request. An authorized active OWNER can use the inbox's blocked-request
resolution to deny a pending request or reassign it **to themselves**, after identity
verification/confirmation. They cannot resolve their own recovery or overwrite an
approved code. Suspended/deactivated accounts are ineligible.

The inbox groups owner work as Pending, Completed and Denied. Completed can include
issued codes and expired requests; check the row's actual outcome. Issuing a code
finishes the OWNER task, not the user's password reset. Never store codes in browser
storage, URLs, logs or shared documents. Deliver clipboard contents privately.

### 8.6 Other protections

| Protection | What it does |
| --- | --- |
| Password hashing | ASP.NET Core PasswordHasher stores a one-way password representation, not readable passwords. Placeholder hashes are rejected. |
| HTTPS / Secure HttpOnly cookies | Encrypt connections and keep authentication cookies out of JavaScript. Cookies use SameSite Strict. |
| CSRF protection | State-changing calls need the `XSRF-TOKEN` cookie and `X-XSRF-TOKEN` header. Obtain `/api/auth/csrf` before mutations using existing helpers. |
| Login throttling | IP and bounded username-based limits slow guessing. In-memory limits are process-local; multi-instance hosting needs further planning. |
| Session revocation | Logout revokes one server session; password/status/security-role changes invalidate account sessions. |
| Account/API checks | Backend requests recheck account status, version and current role. OWNER-only APIs enforce their policy independently of Angular. |

## 9. IMPORTANT: DO NOT MODIFY EXISTING SECURITY IMPLEMENTATION

Authentication, authorization, Users & Roles, dynamic permissions, first-OWNER
setup, sessions and password recovery are implemented. They have isolated tests
and regression checks; this is not a claim of complete live deployment verification.

**Team members must NOT independently modify, replace, remove, bypass or redesign
these mechanisms or their tests.** Do not weaken cookies, disable CSRF, remove
permission checks, bypass login, alter security configuration or edit tests to
hide a failure. Read and use the foundation; build your module around it.

A necessary security change must first go to **both the team leader and security
module owner**. The security owner must handle/review it or explicitly authorize
the change. The named security owner is not recorded reliably here: confirm that
person with the leader before requesting changes.

Paths below are relative to the repository root. Lists with braces group existing
filenames; they are not shell commands.

| Protected area | Existing files/directories covered |
| --- | --- |
| API security | `backend/WoodFlow.Api/Security/` (all middleware, cookie events, policies, CSRF and permission handlers); `Controllers/{Auth,Setup,Users,Permissions,Recovery}Controller.cs` under that project. |
| Application services | `backend/WoodFlow.Application/Services/{AuthenticationService,AccountSessionService,AccountLoginThrottle,SetupService,UserManagementService,UserManagementRules,PermissionService,RecoveryService}.cs`. |
| Interfaces | `backend/WoodFlow.Application/Interfaces/{IAuthenticationService,IAuthSessionRepository,IPasswordService,IUserRepository,IUserManagementRepository,ISetupRepository,ISetupService,IPermissionRepository,IRecoveryRepository}.cs`. |
| DTOs | `backend/WoodFlow.Application/DTOs/{AuthDtos,UserManagementDtos,PermissionDtos,RecoveryDtos}.cs`. |
| Persistence / hashing | `backend/WoodFlow.Infrastructure/Repositories/{UserRepository,UserManagementRepository,SetupRepository,PermissionRepository,PermissionRepository.Users,RecoveryRepository,AuthSessionRepository}.cs`; `backend/WoodFlow.Infrastructure/Security/PasswordService.cs`. |
| Domain security | `backend/WoodFlow.Domain/{User,Roles,AccountStatuses,PermissionCatalog}.cs`. |
| Angular security / account UI | `frontend/src/app/core/auth/`; `features/auth/`, `features/users/`, `features/password-recovery/` under `frontend/src/app/`; shared password control in `frontend/src/app/shared/password-field/`. |
| Security tests | Existing `backend/WoodFlow.Tests/` security tests and test helpers; `frontend/tests/`; `frontend/scripts/check-auth.mjs`. Do not change existing assertions to bypass failures. |
| Database security | Migrations 001–007 and existing user/status/version, permissions, bootstrap, recovery and session definitions/triggers/seeds in `database/schema.sql`. |

**Shared integration files require coordination:** `backend/WoodFlow.Api/Program.cs`,
`backend/WoodFlow.Infrastructure/Database.cs`, project references, `PermissionCatalog.cs`,
`frontend/src/app/app.routes.ts`, `app.config.ts` in that same folder,
`frontend/src/app/layouts/shell/`, `frontend/src/styles.scss`,
`frontend/src/app/shared/_auth-forms.scss`, configuration/proxy/HTTPS scripts,
package/lock files and `database/schema.sql`.
Security-related edits to shared files also require security-owner authorization.

**Your assigned files** are your own feature components/services, business
controllers/services/repositories and approved SQL additions. Being assigned a
business module does not give permission to change shared or security files.

## 10. Use existing security in your module

Follow the existing layering: Api controllers call Application services/interfaces;
Infrastructure implements PostgreSQL repositories; Domain contains business models.
Api references Application and Infrastructure; Infrastructure references Application;
Application references Domain. Do not create circular references. Keep controllers
small. Use dependency injection, parameterized SQL and appropriate transactions.
Register new module services in `Program.cs` only through a coordinated change.

### Backend policy examples

These are snippets for **your new controller**, not instructions to edit existing
security code. Use the policy for the operation, not just a general login check:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Api.Security;

[Authorize(Policy = "inventory.view")]
[HttpGet]
public IActionResult List() => Ok(); // Replace with your own module service.

[Authorize(Policy = "inventory.create")]
[HttpPost]
public IActionResult Create() => NoContent(); // Add validated business handling.

// Use only where the business operation must be OWNER-only:
[Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
[HttpGet("owner-only")]
public IActionResult OwnerOperation() => NoContent(); // Example for your module only.
```

Existing keys: `dashboard.view`; `sales.view/create/edit`;
`quotations.view/create/edit/delete`; `inventory.view/create/edit/delete`;
`production.view/create/edit`; `reports.view`. Slash notation abbreviates separate
keys such as `inventory.create`; it is not a literal policy name.
Ask the security owner to review/add any missing key and its integration/tests.
Never invent an administrative key that can grant ASSISTANT account management.
Each new business API needs its own policy plus object/business-data checks.

### Angular patterns

Coordinate additions to `app.routes.ts`. Import the existing `authGuard`,
`ownerGuard` from `core/auth/auth.guards` and `permissionGuard` from
`core/auth/permission.guard` as appropriate. Under the existing authenticated shell,
your route can use this snippet with your own component loader:

```typescript
canActivate: [permissionGuard],
data: { permission: 'inventory.view' },
```

Use `authGuard` for authenticated routes outside the shell, and `ownerGuard` for
OWNER-only pages. Do not rewrite these guards. In your component, use the actual
service (`PermissionsService` from `core/auth/permissions.service`, using a relative
import appropriate to your file):

```typescript
readonly permissions = inject(PermissionsService);
// Refresh on page entry using the existing RxJS/subscription cleanup pattern.
```

In your template:

```html
@if (permissions.has('inventory.create')) {
  <button type="button">Create item</button>
}
```

Keep relative `/api` calls through Angular HttpClient. Reuse the configured XSRF
support and `AuthService.refreshCsrf()` before state-changing requests, following the
Users & Roles services. Never read/store the HttpOnly authentication cookie or
introduce JWTs. A 401 means login is needed; 403 means access is forbidden; an
outage/503 is not proof of logout or a new installation. Show safe feedback.
**Hiding a button is not enough. The backend must enforce the same permission.**

## 11. FOLLOW THE EXISTING WOODFLOW UI DESIGN

Official [Figma project](https://www.figma.com/design/m6Xv0EYAWLoms9lk0mmVWG/ADBMS-Project-Group-Y?node-id=0-1).
Inspect the actual frames assigned to your module. The **Thisari** page is the
foundation reference. If access is unavailable, ask for access; do not guess.

Use splash, loading, login, initial setup, Users & Roles and recovery screens as
existing code references. Preserve blue/white/light-ash colors, Inter typography,
spacing, buttons, forms, focus states, navigation and responsive layouts. Reuse
local assets and established components. Keep text readable on images.
Use semantic status accents consistently with existing screens.

Do not use PDFs or old HTML wireframes as UI substitutes. Do not redesign another
member's pages, add unrelated palettes/UI libraries or change the sidebar on your
own. Coordinate new shared components and cross-module design changes with the leader.

## 12. Development and ownership rules

Work only on your assigned module. Follow existing standalone Angular components,
feature folders and services. Do not rewrite working infrastructure or perform
unrelated refactoring. Keep changes small and reviewable.

Recorded allocations in README and
[the workload plan](docs/02_modular_architecture_and_workload_division.md):

| Member | Recorded business area |
| --- | --- |
| 1 | Showroom POS / counter billing |
| 2 | Custom quotations and deposits |
| 3 | Carpentry / framework stages |
| 4 | Finishing / delivery stages |
| 5 | Finished furniture inventory |
| 6 | Raw materials / workshop supplies |
| 7 | Employee profiles / base salary work |
| 8 | Piecework / labor allocations |
| 9 | Advances / daily cash ledger |
| 10 | Executive analytics / Audit Center in README |

Confirm current assignments with the leader. Older planning notes have differences
in table names, payroll boundaries and dispatch scope; they are not proof that
those endpoints/features exist. They also mention JWT/vertical-slice infrastructure;
the implemented runtime uses the layered projects and cookie security in this guide.
Audit Logs belongs to its assigned teammate. Do not implement or alter that module,
its data or placeholders without that teammate's approval. Notification/document
sharing placeholders are not implemented business modules.

Ask before editing someone else's files. For shared changes, agree on the owner
and scope first. Before pulling, review/commit your own intended work on your branch.
If Git reports a conflict, inspect both versions with the responsible person. Never
choose “accept all” blindly for schema/security/configuration files. After resolving,
review the final diff and rerun affected checks. Ask for help if uncertain; do not
reset work or force-push to escape a conflict.

## 13. BEFORE PUSHING TO GITHUB — REQUIRED SAFETY CHECKS

Complete all five steps. A successful push is not approval to merge.

### Step 1 — Review changed files

From the root:

```powershell
git branch --show-current
git status --short
git diff --name-only
git diff
git ls-files --others --exclude-standard
```

`git diff` does not show untracked file contents. Open/review those separately.
Check ownership, protected security files, shared configuration and secrets.
Do not paste a diff containing secrets into chat or a pull request.

### Step 2 — Verify portability

Portability means another correctly configured laptop can restore and run your
work. Passing tests on one laptop does not prove it.

- No personal absolute paths, hardcoded passwords or copied local certificates.
- No generated folders, caches or unnecessary machine-specific settings.
- Startup commands still work with Node 22, .NET 10 and local PostgreSQL 16.
- Dependencies restore from the committed lockfile; do not upgrade them casually.
- Database changes preserve existing data and have reviewed explicit migrations.
- Ask another member to follow this guide for changes affecting setup/shared files.

### Step 3 — Run regression checks

Regression testing means checking that your new changes did not break features
that were already working. Stop your API/Angular before builds/restores that may
lock files. From the root:

```powershell
dotnet restore backend/WoodFlow.slnx
dotnet build backend/WoodFlow.slnx --no-restore
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --no-build --no-restore --filter "Category!=PostgreSQL"
npm.cmd --prefix frontend ci
npm.cmd --prefix frontend run build
npm.cmd --prefix frontend run test:auth
```

Run commands one at a time; stop and fix failures. Routine backend tests use isolated
repositories. **Never set `WOODFLOW_TEST_CONNECTION` to your real `woodflow_db`.**
PostgreSQL integration tests can execute SQL/change their target; run them only
with separate explicit approval and a disposable test database.

For existing mocked browser checks, start Angular on trusted HTTPS as section 7
describes. In another terminal, inside `frontend/`:

```powershell
$env:WOODFLOW_TEST_BASE_URL = 'https://localhost:4200'
node tests/browser-auth.mjs
node tests/browser-recovery.mjs
```

These need installed Chrome/Chromium. `BROWSER_PATH` can specify your own browser
executable in this terminal if it is not in the scripts' default location.
Set `WOODFLOW_TEST_BASE_URL` to `https://localhost:4300` if using that port.
All API calls in these scripts are mocked. Generated browser profiles/screenshots
go under ignored `.angular/`. They do not prove real database persistence or live
session revocation.

Also test your new module, affected existing pages, navigation, real permitted and
forbidden actions, and integration with the API using approved local test data.
Do not call a mocked result a live test. Existing style-budget/view-transition
build warnings should be reported; do not hide failures by weakening checks.

### Step 4 — Verify security

- No protected security implementation/tests changed without explicit approval.
- New APIs have correct backend policies and object-level business checks.
- New pages/actions use existing guards and permission checks.
- No login bypass, disabled CSRF, weakened cookie/TLS configuration or secret output.
- Test OWNER/ASSISTANT behavior, including direct unauthorized API calls, as relevant.

### Step 5 — Review Git, then submit only intended changes

```powershell
git status --short --ignored
git check-ignore .env frontend/.certs/localhost.pem frontend/.certs/localhost.key
git ls-files --cached --ignored --exclude-standard
git ls-files
git diff --check
git diff --cached --name-only
git diff --cached
```

Review tracked filenames too: an ignored rule does not protect an already tracked
secret. `git ls-files --cached --ignored --exclude-standard` lists such rule matches,
but is not a complete secret scanner. Inspect contents of your new/changed files.
Never include local appsettings, database dumps, real account data, passwords,
certificates/keys, `node_modules`, `bin`, `obj`, `dist` or `.angular`.

To unstage an accidental file without deleting your working copy:

```powershell
git restore --staged -- <path-to-file>
```

If a secret was already committed, stop and contact the leader/security owner.
Rotate it; merely deleting it from the latest version does not remove Git history.
The earlier shared Docker credential must be treated this way if it was used.

Only after all checks pass, stage exact intended paths, inspect again, then commit
and push your own feature branch (replace placeholders):

```powershell
git add -- <intended-file-1> <intended-file-2>
git diff --cached --name-only
git diff --cached
git commit -m "Describe your module change"
git push -u origin <your-feature-branch>
```

Avoid blanket staging. Open a GitHub pull request to the agreed target branch.
Describe changes, tests, portability and any manual migration steps. The leader
must review shared infrastructure changes; the security owner must review approved
security changes. Do not merge without review or force-push. If portability,
regression or security verification fails, fix it before submitting.

## 14. Common errors

| Problem | What to do |
| --- | --- |
| `psql` not found | Use the full PostgreSQL 16 `bin` path in section 4. Do not assume it is on PATH. |
| PostgreSQL is stopped | Check `Get-Service *postgres*`; start your PostgreSQL 16 service in Windows Services. |
| Incorrect database credentials / health unavailable | Test the same local login with psql/pgAdmin. Re-enter section 5 in the API terminal. Check host, port, database and username. Do not print the connection string. |
| Database already exists | Stop initialization. Check its contents/state; do not drop it or rerun schema to make the command pass. |
| Missing tables | Health alone does not verify tables. Check section 4.3. For existing/partially imported databases ask the leader which repair/upgrade is safe. |
| PostgreSQL port conflict | Native PostgreSQL and Docker cannot both bind 5432. Identify your intended server; do not stop/delete someone else's database. Coordinate non-default ports and private connection settings. |
| .NET target unsupported | Install .NET **10 SDK**, reopen terminal, check `dotnet --list-sdks`. Runtime-only or SDK 8 is insufficient. |
| Node engine mismatch | Use Node **22** and npm 10+. Reopen terminal. Do not change package requirements to suppress the error. |
| `npm ci` failure | Check Node/npm, network and lockfile. Run inside `frontend/` or use `--prefix frontend` from root. Do not delete the lockfile or upgrade packages as a shortcut. |
| `frontend/frontend/package.json` missing | You are already inside `frontend/`. Run `npm.cmd start` there; do not add another `--prefix frontend`. |
| EPERM / locked esbuild, DLL or native file | Ctrl+C your own running Angular/API. Retry restore/build. Do not delete source, certificates or database files. Ask for help if a lock remains. |
| Certificate warning / missing PEM or key | Follow section 6 using your own Windows account. Trust/check the certificate, export both files, restart browser and servers. Do not disable verification or copy keys. |
| Proxy error / backend unreachable | Start HTTPS API on 7001. Check its direct health URL, then frontend `/api/health`. Confirm both processes use the matching trusted certificate; restart Angular after export. |
| Frontend connection refused | Keep `npm start` running and use its printed HTTPS port: normally 4200, optionally 4300. |
| Login fails | Check health, exact username/password and ACTIVE status. Demo hashes cannot log in. Use first setup only for an uninitialized installation; otherwise use recovery. Wait if throttled. |
| 401 / 403 | 401 means a valid login session is needed. 403 means the role/permission is insufficient. Ask the OWNER; never remove backend checks to fix it. |
| CSRF / invalid request | Use relative `/api`, HTTPS and the existing CSRF helper before mutations. Refresh tokens after identity changes. Do not disable the filter. |
| Git includes local files | Stop before committing. Inspect tracking and ignores, unstage only the intended paths, and ask the leader about already tracked secrets. Do not delete your working credentials/certificates. |

## 15. Final developer checklist

- [ ] Required software and correct major versions installed.
- [ ] Repository cloned; personal feature branch created.
- [ ] Own local PostgreSQL database initialized safely.
- [ ] Private backend credentials configured.
- [ ] Own HTTPS certificate trusted and exported.
- [ ] Backend and frontend running; direct/proxy health connected.
- [ ] Existing login and one-time OWNER setup understood.
- [ ] Security ownership rules understood; named security owner confirmed.
- [ ] Assigned Figma frames reviewed; existing design preserved.
- [ ] Only assigned files changed; shared changes coordinated.
- [ ] Portability checks completed; regression/security tests passed.
- [ ] Manual tests and untested limitations recorded honestly.
- [ ] Git changes and staged contents reviewed; no secrets/generated files.
- [ ] Feature-branch pull request ready for review, not automatic merging.
