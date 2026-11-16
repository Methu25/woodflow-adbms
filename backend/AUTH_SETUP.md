# Authentication and first OWNER setup

Session security update: existing installations need outstanding migration 007 before new logins; fresh schema includes it.
Logout now revokes the current PostgreSQL-backed session; pre-upgrade cookies require sign-in again.
See [SECURITY_FIXES.md](SECURITY_FIXES.md) for exact migration commands, login limits, and verification.

All commands below run from the repository root. No migration, account creation,
or setup-code generation occurs at application startup. Each developer uses their
own **local PostgreSQL 16** database. Never share database accounts or setup codes.

## Manual database preparation

For a NEW empty database, follow
[the database guide](../database/team_database_setup_guide.md): apply the
consolidated `database/schema.sql` once, with no subsequent migrations 001–007.
For existing databases, back up first and apply only outstanding migrations
001–007 in numeric order with the backend stopped. Never reapply the full schema.
The commands in individual feature guides describe upgrades, not fresh installs.

Set the local connection address for the configuration command below:

```powershell
$env:PGHOST = "localhost"
$env:PGPORT = "5432"
```

001 maps CASHIER and WORKSHOP_SUPERVISOR to ASSISTANT, keeping IDs and other
columns, and replaces the role check. 002 creates just owner_setup_state:
one installation row holding a SHA-256 code hash, expiry, consumption time,
and a durable initialization marker/FK. It stores no plaintext code or password.
Both migrations are transactional; an unknown existing role makes 001 fail and
roll back instead of discarding data.

Existing placeholder accounts remain intact and cannot log in. Use an unused
username for the new OWNER; setup never overwrites an existing username.
No seed cleanup is required to bootstrap. Before real use, a trusted DBA should
review and remove or deactivate demo accounts after checking foreign keys.
Do not replace seed hashes with a shared password. An OWNER with a structurally
valid ASP.NET Identity hash blocks bootstrap even when inactive. Such an
installation needs an administrator-led recovery process, not first-owner setup.
That recovery workflow and user-management CRUD are outside this task.

## Configure the backend and HTTPS

The backend reads ConnectionStrings:WoodFlowDb from ASP.NET Core configuration.
For a local PowerShell session, avoid putting passwords in source or shell history:

```powershell
$dbCredential = Get-Credential -Message "Your local PostgreSQL credentials"
$dbPassword = $dbCredential.GetNetworkCredential().Password.Replace('"', '""')
$dbUsername = $dbCredential.UserName.Replace('"', '""')
$env:ConnectionStrings__WoodFlowDb = 'Host=' + $env:PGHOST + ';Port=' + $env:PGPORT + ';Database=woodflow_db;Username="' + $dbUsername + '";Password="' + $dbPassword + '"'
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet dev-certs https --trust
dotnet build backend/WoodFlow.slnx --configuration Release
```

URLs and connection strings are configurable. Examples use local addresses solely
for development. PostgreSQL must remain private. AllowedHosts is configurable
through the standard ASP.NET Core configuration/env override. Never enable
credentialed wildcard CORS. No CORS policy is enabled; use same-origin requests.

## Issue the first-owner authorization

Only a trusted administrator with server/database access runs:

```powershell
dotnet run --project backend/WoodFlow.Api --configuration Release --no-build --no-launch-profile -- --generate-owner-setup-code
```

The command does not start the web server or create an account. It prints the
256-bit random code once to the administrator's terminal, stores only its hash
with a **10-minute database-clock expiry**, and refuses after initialization.
Treat that terminal output as a secret: do not redirect it to logs, paste it into
chat, check it into source, or share screenshots. Generating a replacement code
invalidates any earlier code. No HTTP endpoint can issue codes.

Then start the API:

```powershell
dotnet run --project backend/WoodFlow.Api --configuration Release --no-build --no-launch-profile -- --urls https://localhost:7001
```

HTTP remains usable for /api/health only. All other requests require HTTPS;
credentials submitted over HTTP are rejected, not redirected. Restart any
previously running API to load the new code. Release builds avoid Debug DLL
locks if an older development server is still running.

The existing Angular First OWNER Setup screen uses /initial-setup. Follow
[frontend/README.md](../frontend/README.md) for trusted local HTTPS certificates
and the verified HTTPS /api proxy to port 7001.

A 401 from /api/auth/me means unauthenticated, not uninitialized. A new browser
must not trigger owner setup based on local storage or missing cookies. There is
no public initialization-status endpoint; the trusted administrator directs the
first owner to setup and supplies the one-time authorization.

## API contract and CSRF handshake

- GET /api/auth/csrf (anonymous, HTTPS): 204; issues HttpOnly secure
  __Host-WoodFlow.Csrf and readable secure XSRF-TOKEN cookies. Contains no
  installation-state information.
- POST /api/setup/initialize-owner (anonymous with one-time authorization):
  fullName (1–100 nonblank characters), username (3–50 ASCII letters/digits or
  underscore/dot/hyphen), password (12–128 characters), confirmPassword,
  setupCode (64 hex characters). Success: 204, then use normal login.
  Failure: generic 400. No automatic sign-in or authorization code in responses.
- POST /api/auth/login: username, password. Success: safe account JSON and
  HttpOnly Secure SameSite=Strict session cookie; bad credentials: generic 401.
  Exact username matching follows the existing case-sensitive database unique key.
- POST /api/auth/logout (authenticated): 204; clears the authentication cookie.
- GET /api/auth/me (authenticated): userId, username, fullName, role only.
  Unauthenticated: 401; forbidden role: 403.

Before every login or setup, GET /api/auth/csrf using the SAME browser/cookie
session. Send the readable XSRF-TOKEN value in the X-XSRF-TOKEN header on every
state-changing request. Angular HttpClient uses those default names for relative
same-origin /api calls once HTTP support is configured in the future UI.
After login AND logout, fetch a fresh CSRF token before the next state change:
tokens are identity-bound. Do not reuse the anonymous token after login.
JavaScript must never attempt to read the HttpOnly authentication cookie.

Login is limited to 5 attempts/minute per remote IP. Setup is limited globally
to 3 attempts/15 minutes per server process. Exhaustion returns 429; wait for the
window instead of regenerating codes to bypass a limit. Invalid request shapes
return generic 400. Missing database configuration/connectivity returns generic
503 without exception details. The health endpoint remains anonymous.

Cookies expire after 8 hours with no sliding renewal. Each authenticated request
reloads the account, rejects inactive/unknown roles/placeholder hashes, uses the
current database role, and rejects cookies whose password fingerprint changed.
Logout clears the browser cookie; as with ordinary stateless cookie auth, a
previously copied cookie remains usable until expiry/account deactivation/password
change. Per-session server-side revocation is not implemented.

## Policies and implementation boundaries

Domain Roles defines exactly OWNER and ASSISTANT.
OwnerOnly requires OWNER; BusinessAccess allows both. A fallback policy protects
new endpoints unless explicitly marked AllowAnonymous. Future administrative
controllers must use [Authorize(Policy = AuthorizationPolicies.OwnerOnly)];
business controllers use BusinessAccess. No user-management endpoints exist yet.

Application owns DTO validation and services/interfaces; Infrastructure owns
parameterized PostgreSQL repositories and PasswordHasher; Api owns cookies,
policies, CSRF, HTTPS enforcement and rate limits. No JWT/external auth is used.
PasswordHasher uses Identity V3 PBKDF2-HMAC-SHA512 at 210,000 iterations.
Usable older Identity hashes are upgraded after successful verification.
Placeholder/malformed hashes always fail.

Both code issuance and initialization acquire the same transaction-scoped
PostgreSQL advisory lock. Owner checks, token validation, user insertion, and
token consumption/initialization marker occur under that lock. Username conflicts
roll back without consuming the code. The marker remains even if the first
OWNER is later deactivated or changed to ASSISTANT. The first OWNER's FK prevents
deleting that referenced account. DB administrators must not reset the marker.

ASP.NET Core Data Protection protects the auth and antiforgery cookies. Keep
its key material private and persistent for stable sessions; configure a shared,
protected key ring before any future multi-instance deployment. Do not enable
HTTP request-body or database-parameter logging. Future trusted reverse-proxy
configuration must explicitly constrain forwarded headers/proxies; this task
does not trust arbitrary client-supplied forwarded headers.

## Tests

Routine verification never connects to PostgreSQL:

```powershell
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --configuration Release --filter "Category!=PostgreSQL"
```

Tests use real ASP.NET Core cookie/CSRF/authorization middleware with in-memory
repository replacements, plus password and setup-service tests. Test-only probe
controllers are added exclusively by the test host.

An additional PostgreSQL integration test is **opt-in and not run automatically**.
It exercises both migrations twice, preserves seed IDs, checks expired/rotated
codes, username collisions, replay, eight concurrent setup attempts, and the
durable initialization marker. It DROPS/recreates two tables, so it only accepts
an explicitly supplied local disposable database named woodflow_auth_tests.

After explicit approval, create that separate empty local PostgreSQL 16 database,
set WOODFLOW_TEST_CONNECTION to its connection string (not woodflow_db), then:

```powershell
$env:WOODFLOW_RUN_DB_TESTS = "1"
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --configuration Release --filter "Category=PostgreSQL"
Remove-Item Env:WOODFLOW_RUN_DB_TESTS
Remove-Item Env:WOODFLOW_TEST_CONNECTION
```

Do not claim database concurrency/migration runtime verification until that test
has actually been run. No migrations or account creation were executed as part
of implementing this task.

When finished, clear local credentials:

```powershell
Remove-Item Env:ConnectionStrings__WoodFlowDb
Remove-Variable dbCredential, dbPassword, dbUsername
```
