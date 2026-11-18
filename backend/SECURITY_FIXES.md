# Session revocation, login throttling, and blocked recovery requests

## Manual migration and restart

For existing databases missing 007 only (fresh consolidated `schema.sql` already includes it): back up your local database. From the repository root in PowerShell (adjust the PostgreSQL executable/login if needed):

```powershell
& "$env:ProgramFiles\PostgreSQL\16\bin\psql.exe" -h localhost -p 5432 -U postgres -d woodflow_db -v ON_ERROR_STOP=1 -f database/migrations/007_session_revocation_and_recovery_resolution.sql
```

Enter the database password at the prompt, never in the command. Apply 003 and 006 first if not already applied.
007 is transactional/rerunnable and creates `auth_sessions` plus recovery-resolution metadata. It changes no roles,
passwords or business records. Nothing applies this migration at startup.

Restart the API using the existing local connection configuration:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/WoodFlow.Api --configuration Release --no-launch-profile -- --urls https://localhost:7001
```

Refresh Angular. **Everyone must sign in again after upgrading:** pre-upgrade cookies contain no server session handle
and are rejected, rather than accepted through a compatibility bypass. New logins require migration 007.
An unavailable session database fails closed with generic 503; no in-memory production fallback is used.

## Session behavior

Each successful login creates an independent, cryptographically random 256-bit session handle inside the encrypted,
HttpOnly cookie. Only its SHA-256 hash, user ID, account version, lifetime, and revocation timestamp are stored in PostgreSQL.
The handle is never returned in the account DTO or logs. Sessions last at most eight hours, without sliding renewal.
Creation rechecks the verified account's password hash/version/status. Every authenticated request checks both the
existing account credential stamp and the server-side session's expiry/revocation state.

`POST /api/auth/logout` revokes **only the current session** before removing its cookie and returning success. A copied
cookie from that session fails on subsequent authenticated requests; a separate browser/device login stays signed in.
An already-authorized in-flight request is not retroactively cancelled. Repeated logout may return 401 after revocation,
which the existing Angular logout flow already treats as signed out.

If revocation fails, the endpoint does not falsely report success or delete a still-valid cookie. Suspension, deactivation,
password reset, and role changes retain their existing account-wide credential/version invalidation. Changing permissions
continues to take effect through live permission reads. This change adds no session-list UI or Audit Logs implementation.

Expired session rows are not reusable. The expiry index supports a future scheduled maintenance task to prune them;
no maintenance job or automatic deletion of business data is introduced here.

## Login protection

Three independent limits apply before issuing sessions:

* Existing per-IP maximum: 5 login requests per minute.
* Installation-wide maximum: 120 login requests per minute (including malformed/rejected calls).
* Per-normalized-username token bucket: burst of 10 password-verification attempts, refilling one attempt every 6 seconds.

The account bucket is shared across source IPs. It counts both successful/failed attempts, applies identically to known
and unknown usernames, and returns a generic 429 with a short `Retry-After`. Usernames are trimmed/uppercased only for
throttling; existing login matching is unchanged. Rejected attempts cannot extend the refill delay, and the mechanism
does not deactivate accounts, invalidate other sessions, or require an administrator to unlock a user.

Only hashes of normalized usernames are kept in memory, not raw usernames, passwords, IP/account lists, or credentials.
The store is bounded to 5,000 entries; idle entries expire after ten minutes when capacity cleanup is necessary. At
capacity it rejects new bucket keys temporarily instead of evicting active keys and allowing limit bypass. The global
cap also limits random-username flooding. A sustained attack can still compete with a legitimate user's attempts:
no account-aware limiter can promise zero availability impact under attack.

This is suitable for the current **single API process**. Counters reset on process restart and are not shared between
future replicas. Before multi-instance hosting, use shared atomic limits and a private shared ASP.NET Data Protection
key ring; PostgreSQL session revocation itself is already shared. Configure trusted proxy forwarding explicitly rather
than trusting arbitrary IP headers. No test-only `X-Test-IP` header is honored by the production API.

**Password limits were deliberately left unchanged at the user's request:** user creation/recovery use 8–128 characters;
first-OWNER setup uses 12–128; login continues accepting existing passwords. Weak-password rejection remains a separate
unresolved audit finding. No password was silently changed or rehashed except existing normal login behavior.

## OWNER resolution of misdirected pending requests

The existing inbox now includes **Resolve a blocked request**, using the existing dialog design. An active OWNER
must verify the requester through a trusted outside channel, enter the exact username, and explicitly confirm either
**Assign to me** or **Close pending request**. This is an intentional, narrowly scoped OWNER escalation capability;
ordinary inbox/details/approval still expose only assigned requests.

Backend contract:

```text
POST /api/password-recovery/resolve-pending
{ "username": "<verified username>", "resolution": "REASSIGN_TO_ME" | "DENY",
  "identityVerified": true, "confirmed": true }
```

Requires HTTPS, an active OWNER session, CSRF (`GET /api/auth/csrf` then `X-XSRF-TOKEN`), and rate limiting. ASSISTANT
gets 403; unauthenticated callers get 401. Identity verification remains a human responsibility; a checkbox cannot prove it.

The transaction uses the existing account/recovery lock order, rechecks both accounts, and only changes an unexpired
**PENDING** request belonging to another active user. The OWNER cannot resolve their own request. Claiming preserves
the requester, request ID, expiration, and account version; it changes the assigned OWNER to the actor. Closing retains
the row as DENIED and records the actor. Previous assignment/resolution metadata is retained on the same request row.

APPROVED, COMPLETED, DENIED and EXPIRED requests cannot be overridden by this endpoint. Concurrent approval and
reassignment are serialized: once approved, reassignment fails; once reassigned, the former OWNER cannot approve.
No code is exposed or generated by resolution. Anonymous submission still gives the same generic acknowledgment and
cannot redirect, cancel, extend, or overwrite an existing open/approved request. Existing five-minute submission
cooldown still applies after denial; taking over a pending request avoids needing a new public submission.

## Verification

```powershell
dotnet build backend/WoodFlow.slnx
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --filter 'Category!=PostgreSQL'
npm.cmd --prefix frontend run build
```

HTTP tests use real cookies, CSRF, and policies with test repositories. New checks cover copied-cookie replay after
logout, other sessions, revocation outage, expiry, account limits across test IPs, cooldown/refill/concurrency/bounded
memory, unchanged password limits, and resolution authorization/confirmation. Production does not contain test IP middleware.

`PostgresRecoveryTests` additionally covers migration reruns, persisted sessions/revocation/expiry, reset invalidation,
pending-request reassignment/denial, self-resolution rejection, anonymous overwrite rejection, approved-code protection,
and approval/reassignment races. It requires explicitly configured, disposable local PostgreSQL 16 database
`woodflow_recovery_tests` via `WOODFLOW_RECOVERY_TEST_CONNECTION`; it must never use `woodflow_db`.
These PostgreSQL integration tests are opt-in and were not run automatically. The browser recovery suite mocks every
API call and cannot prove PostgreSQL behavior.
