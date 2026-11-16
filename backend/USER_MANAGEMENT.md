# Users & Roles — Part 1

OWNER-only Angular route: `/users`. Existing roles remain OWNER and ASSISTANT.

## Required manual database step

Stop the backend, then run from the repository root in PowerShell (PostgreSQL 16 client on PATH):

```powershell
psql -h localhost -p 5432 -U postgres -d woodflow_db -v ON_ERROR_STOP=1 -f database/migrations/003_user_account_status.sql
```

Use your local PostgreSQL administrator name instead of `postgres` if different. Enter the database password privately when prompted. This migration is **never run by the application**. It is transactional and rerunnable. It adds only account status/session-version storage and a compatibility trigger; it does not change roles or delete records. Existing `is_active = false` or NULL accounts become DEACTIVATED; review these mappings before applying. Existing active accounts remain ACTIVE. Fresh installations use only consolidated `schema.sql`, which already includes migrations 001–007. This command is for existing databases missing 003.

The API requires the migration before authentication/user management can query the new columns. Restart using your existing HTTPS launch/configuration from `AUTH_SETUP.md`, and sign in again: session stamps now include the account version. No accounts or passwords are created automatically.

## API

All routes require HTTPS, a valid OWNER cookie, and no-store responses. All mutations require the existing CSRF cookie/header obtained from `GET /api/auth/csrf`.

- `GET /api/users/counts`
- `GET /api/users?category=owners|assistants|suspended|deactivated&search=&page=1&pageSize=10` (maximum page size 50)
- `GET /api/users/{id}`
- `POST /api/users`: `fullName`, `username`, `password`, `role`
- `PATCH /api/users/{id}`: `expectedStatus`, `expectedRole`, `expectedVersion`, `status`, `role`, `confirmed: true`

Usernames use 3–50 ASCII letters, numbers, `.`, `_`, `-`. New usernames are checked case-insensitively against every retained account; existing login matching remains unchanged. New-user passwords must contain 8–128 characters, allow letters, numbers and symbols, cannot be only whitespace, and use the existing PasswordHasher. Existing passwords and first-owner setup rules are unchanged. Responses contain only safe account fields, created date and concurrency version.

Suspension is reversible; deactivation cannot be reversed through the API or compatibility trigger. No account deletion endpoint exists. Status/role changes increment `auth_version`, invalidating existing cookies on their next request; reactivation does not revive old cookies. Already-running requests cannot be recalled. Self-suspension/deactivation/demotion is blocked. The last active OWNER with a usable password hash is protected. Management changes share the bootstrap advisory lock and a users-table write lock, recheck the actor and target inside the transaction, and reject stale versions. Client confirmations supplement backend checks.

## Audit ownership / future integration

No Audit Logs module files, database tables, records, services or pages are changed or created. The User Activity Logs card remains a disabled placeholder. Manage Permissions and individual assistant access are now implemented; see `PERMISSIONS.md`. Transaction-boundary comments in `UserManagementRepository` identify the future integration point: actor ID, safe before/after account details and operation, before commit. The Audit Logs teammate must implement that integration later; **account management events are not yet recorded**. Never pass passwords or hashes to auditing.

## Verification

```powershell
dotnet build backend/WoodFlow.slnx
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --filter "Category!=PostgreSQL"
npm.cmd --prefix frontend run build
```

Routine API tests use isolated in-memory repositories and ephemeral cookie keys, never `woodflow_db`. PostgreSQL integration tests are opt-in and must use a separate disposable local test database; do not point them at business data. No migrations or live account actions are run during routine builds/tests. Browser/live PostgreSQL verification must be distinguished from these tests.
