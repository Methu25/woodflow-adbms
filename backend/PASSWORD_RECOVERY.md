# OWNER-assisted password recovery (backend)

Security update: [SECURITY_FIXES.md](SECURITY_FIXES.md) documents migration 007 and the verified-OWNER
`POST /api/password-recovery/resolve-pending` escalation for misdirected pending requests. Ordinary
inbox/details/approval remain assigned-OWNER-only. Resolution never overwrites an approved code.

This adds no email delivery, frontend, automatic accounts, audit module, or administrator bypass.
Use the existing HTTPS backend, cookie authentication, and database configuration. Keep PostgreSQL private.

## Manual database update

Back up the local `woodflow_db` first. From the repository root in PowerShell, with PostgreSQL 16's normal
installation path (adjust the executable path or database login if your installation differs):

```powershell
& "$env:ProgramFiles\PostgreSQL\16\bin\psql.exe" -h localhost -p 5432 -U postgres -d woodflow_db -v ON_ERROR_STOP=1 -f database/migrations/006_password_recovery.sql
```

Enter the database password only at the interactive prompt. Migration 003 must already be applied.
006 is transactional and rerunnable; it creates one recovery table and its indexes, without role changes.
Fresh databases use only consolidated `schema.sql`, which includes 006 and 007; this command is for existing databases missing 006. Nothing applies migrations automatically.

## API contract for the frontend teammate

All paths below start with `/api/password-recovery`. Responses are `no-store`.
Before **every** POST flow obtain the existing `GET /api/auth/csrf` cookies and send the
`X-XSRF-TOKEN` header with the `XSRF-TOKEN` cookie value. Send relative same-origin HTTPS requests.
Refresh CSRF after login/logout as before. No frontend is changed by this task.

| Method/path | Authorization / body / response |
|---|---|
| GET `/owners` | Public, rate limited; only `{ownerId, fullName}` contacts for usable active OWNERs. No username, email, password, account status, or permission data. |
| POST `/requests` | Public; `{username, ownerId}`; generic 202 acknowledgment even for unknown, inactive, self-selected, ambiguous-case usernames, or duplicate requests. No request ID or status lookup is returned. |
| GET `/requests?status=PENDING&page=1&pageSize=10` | OWNER, assigned requests only. Optional status filter; maximum page size 50. Safe requester details and lifecycle timestamps only. |
| GET `/requests?category=completed&page=1&pageSize=10` | OWNER work-category filter: `pending`, `completed`, `denied`. Completed groups APPROVED/COMPLETED/EXPIRED without changing reset-code validity. Existing exact-status filtering remains supported. |
| GET `/pending-count` | OWNER; `{pendingCount}` for assigned, unexpired pending requests. |
| GET `/requests/{requestId}` | OWNER, assigned request only. Unassigned/self request returns 404. |
| POST `/requests/{requestId}/deny` | Assigned active OWNER; no body required; pending only; 204. |
| POST `/requests/{requestId}/approve` | Assigned active OWNER; `{identityVerified:true}`; pending only. Returns `{code, expiresAt}` **once**. |
| POST `/complete` | Public; `{code, newPassword, confirmPassword}`; 204 on success, generic 400 otherwise. Password 8–128 characters, not whitespace-only. |

OWNERs must verify identity **outside the application** before approval, then privately give the code to
that person. The API requires explicit confirmation but cannot prove a human performed that check.
Treat the reset code as a password: do not log it, cache it, put it in URLs, or store it in browser storage.
The future frontend should show it once, then discard it. Do not enable request/response body logging on these APIs.
Other OWNERs cannot read or process an assigned request. ASSISTANT receives 403 for administration; anonymous users receive 401.
There is no public recovery-status endpoint and no automatic sign-in after reset.

## Expiration, races, and failures

* Pending requests expire in one hour. Only one PENDING/APPROVED request per account is allowed; repeat submissions
  do not extend or replace it. At most one new request per account per five minutes is accepted silently.
* Approval generates 32 cryptographically random bytes (64 hex characters). Only SHA-256 of those bytes is stored.
  Codes expire ten minutes after approval. Repeated approval returns 409 and never reveals/reissues the code.
* PostgreSQL transactions use the same advisory lock and users-table lock order as user management. A unique partial
  index also prevents duplicate open requests. Approval/denial races have one winner; concurrent resets have one winner.
* Reset atomically replaces the password using the existing PasswordHasher, increments `auth_version`, consumes the code,
  clears its hash, and completes the request. Existing cookies are rejected on their next authenticated request.
* Inactive, unsupported-role, and placeholder-hash accounts are ineligible. Suspension, deactivation, requester security-version
  changes, or approving OWNER demotion/suspension/security-version changes invalidate outstanding authorizations.
* Expiration is enforced on every use; EXPIRED rows/hash cleanup are updated lazily during recovery inbox or write operations.
  There is no background scheduler. Terminal records are retained; retention policy can be decided later.
* If a code is lost or the approval response is lost, it cannot be retrieved. Wait for its ten-minute expiry and submit
  a fresh request. Pending requests cannot be reassigned publicly: wait for expiry or ask the assigned OWNER to deny.
  Invalid/expired code and weak-password attempts do not consume a valid code; correct the input before expiry.
* Database/transport failures return generic 503. A lost successful reset response may still mean the password changed;
  try normal login with the new password. Request submission does not invalidate a working password/session.
* Public submission/contact calls share a per-IP 10-per-ten-minute cap; verification has 10/minute per IP; approval
  has 10/minute per IP. Public recovery also has a shared 60/minute cap. These counters are in-memory per API instance;
  a future multi-instance deployment needs shared limits. Behind a proxy, configure trusted forwarded headers explicitly.
  The 500ms minimum submission response reduces ordinary timing differences; it is not a constant-time network guarantee.

If every OWNER is inaccessible, this flow cannot help. A separately designed, trusted server-administrator recovery
procedure is required. Do not reopen first-OWNER setup, edit password hashes manually, or add a public bypass.
The repository has transaction-boundary comments for future audit integration; no audit functionality is implemented.

## Verification

```powershell
dotnet build backend/WoodFlow.slnx
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --filter 'Category!=PostgreSQL'
```

The ordinary tests use test doubles for database access. They check real HTTP authorization/CSRF/HTTPS/rate limiting,
generic responses, code generation/hashing, password hashing, and cookie invalidation. They do not prove PostgreSQL persistence.

`PostgresRecoveryTests` is opt-in and refuses any database except an **empty**, dedicated local PostgreSQL 16 database
named `woodflow_recovery_tests`. It creates and removes only its fixture tables. Never use `woodflow_db` for this test.
After separately authorizing/creating that disposable database and configuring its connection privately:

```powershell
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --filter 'FullyQualifiedName~PostgresRecoveryTests'
```

It requires the `WOODFLOW_RECOVERY_TEST_CONNECTION` environment variable. Without it, the test is skipped.
It covers migrations/reruns, Assistant and OWNER recovery, self/unassigned/inactive rejection, duplicates, concurrent
approval/denial/reset, expiry/reuse, password persistence and session-stamp changes. It is not run automatically here.
