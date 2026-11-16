# Assistant permissions — Part 2

## Manual migration (existing databases only)

Fresh installations use only consolidated `schema.sql`, which includes 001–007. The following upgrade commands apply only to existing databases missing these migrations. The existing database must already have Part 1 migration 003. Stop the backend and apply **only** migration 004 to your local PostgreSQL 16 `woodflow_db`:

```powershell
psql -h localhost -p 5432 -U postgres -d woodflow_db -v ON_ERROR_STOP=1 -f database/migrations/004_assistant_permissions.sql
```

If `psql` is not on PATH, select `woodflow_db` in pgAdmin, open Query Tool, open that migration and execute the entire file. Do not rerun `schema.sql` on an existing database. Restart the backend normally afterward. Credentials remain in your existing environment/configuration.

The transaction creates one singleton `assistant_permission_settings` table containing `granted_keys TEXT[]` and a BIGINT concurrency version. It does not change roles, accounts or existing module tables. Default is **Dashboard View only**; every other grant is opt-in. Rerunning the migration preserves saved settings. Fresh installations include the same table in `schema.sql`. No migration runs at application startup.

## API / enforcement

- `GET /api/permissions/assistant`: OWNER-only catalog, grants and version.
- `PUT /api/permissions/assistant`: OWNER-only body `{ grantedKeys: string[], expectedVersion: number, confirmed: true }`; uses existing CSRF protection. Stale version returns 409.
- `GET /api/permissions/me`: authenticated account's effective business keys; OWNER always receives every key.
- `GET /api/permissions/check/{key}`: real backend authorization check; 204 granted, 403 denied, 401 unauthenticated, 400 unknown key.

All endpoints require HTTPS and use no-store responses. All role-wide saves use parameterized SQL, a transaction, the same advisory lock/order as user management, actor revalidation and a locked settings row. Unknown keys, duplicates, administrative keys, unconfirmed updates and non-View actions without View are rejected. Empty grants are valid. OWNER access is fixed outside this matrix. OWNER-only user management, permission administration and existing setup/authentication boundaries remain intact. Permissions never grant security/audit administration.

The authorization handler reads current database settings on **every protected request**, not from cookie claims or a process cache. Existing assistant sessions therefore honor grants/revocations without logging out. Already-executing requests cannot be recalled. The Angular route guard refreshes effective keys and calls the backend check; sidebar checks are navigation assistance, not a security boundary. Failed permission reads fail closed.

## Module integration responsibilities

`PermissionCatalog` defines these keys centrally:

| Module | Keys |
|---|---|
| Dashboard | `dashboard.view` |
| Sales & Billing | `sales.view`, `sales.create`, `sales.edit` |
| Quotations | `quotations.view`, `quotations.create`, `quotations.edit`, `quotations.delete` |
| Inventory | `inventory.view`, `inventory.create`, `inventory.edit`, `inventory.delete` |
| Production | `production.view`, `production.create`, `production.edit` |
| Reports | `reports.view` |

Only dashboard **navigation** is currently connected. It is a static welcome page with no business-data API. Other modules' endpoints are **not implemented or modified by Part 2**, so saving their grants does not claim those endpoints are already protected. An allowed key does not itself implement a route/module.

When building each module, teammates must apply the named backend policy to each actual controller action, for example `[Authorize(Policy = "sales.create")]`. Do not rely on the role-only `BusinessAccess` policy for business operations. Apply the matching Angular `permissionGuard` with route data `{ permission: 'sales.view' }` and use `PermissionsService.has(key)` to present action controls. The handler gives OWNER access and evaluates the current ASSISTANT settings. Business rules still apply: invoice retention, draft-only deletion, referential integrity, validation and transaction boundaries must not be bypassed by permission grants. No permission is defined for deleting posted billing/production history.

The Access Restricted route remains available to authenticated users with no business grants, as do session checks and logout. Never permission-gate these recovery/session endpoints.

## Audit integration boundary

No Audit Logs pages, tables, controllers, repositories, services or records were created or modified. The existing User Activity Logs card remains a disabled future placeholder. A comment before the save commit in `PermissionRepository` identifies the future Part 3 transaction integration point: actor ID and safe before/after keys/version. Logging is intentionally not implemented here.

## Verification

```powershell
dotnet build backend/WoodFlow.slnx
dotnet test backend/WoodFlow.Tests/WoodFlow.Tests.csproj --filter "Category!=PostgreSQL"
npm.cmd --prefix frontend run build
```

Routine HTTP tests use actual cookie/CSRF/policy middleware with in-memory boundaries; they do not verify live PostgreSQL persistence. The optional PostgreSQL test requires explicit opt-in and a separate, empty disposable local database named `woodflow_permissions_tests`. It never uses `ConnectionStrings__WoodFlowDb`; never point test settings at shop/developer business data. Live persistence/concurrency checks must be reported separately from isolated tests.

## Individual assistant access (extension)

From Users & Roles, each non-deactivated ASSISTANT row has **Manage Permissions**, opening `/users/{userId}/permissions`. The View drawer shows effective permissions, whether they are inherited/custom, and the same management shortcut. OWNER profiles show fixed full access instead. Deactivated accounts are read-only.

Apply the additional migration manually after 004, with the backend stopped:

```powershell
psql -h localhost -p 5432 -U postgres -d woodflow_db -v ON_ERROR_STOP=1 -f database/migrations/005_assistant_user_permissions.sql
```

Alternatively execute the entire 005 file in pgAdmin Query Tool for `woodflow_db`. Restart the backend afterward. The migration is rerunnable and never applied automatically. No roles or existing accounts are replaced. Fresh `schema.sql` includes the same definitions.

The new `assistant_user_permissions` table has one optional row per user, a nullable `granted_keys` array and a concurrency version. NULL/absent means **inherit current role defaults**; an empty array is an explicit override granting no business access. Custom grants replace (not merge with) defaults, so an OWNER can either restrict or extend one assistant's business access. Role-default edits only affect inheriting accounts.

- `GET /api/permissions/users/{id}`: OWNER-only catalog, safe account identity, effective grants, inheritance flag, account version, override version and role-default version.
- `PUT /api/permissions/users/{id}`: OWNER-only, CSRF protected. Body: `grantedKeys`, `expectedVersion`, `expectedRoleVersion`, `expectedAccountVersion`, `useRoleDefaults`, `confirmed`. Use `useRoleDefaults: true` with empty `grantedKeys` to reset.

Save checks account/role/override versions under the existing management advisory lock, users write lock and locked role-settings row. Reset retains a versioned row to prevent stale-write/override-reset cycles. The role-change trigger clears existing overrides on promotion/demotion, so old grants cannot reappear after a later demotion. Suspension/reactivation preserves permissions, but normal account-status enforcement blocks suspended sessions. Deactivated users cannot receive changes; OWNER targets and missing users are rejected.

The same backend permission-check policies now resolve **that authenticated assistant's effective grants** on every request. OWNER access and administrative exclusions remain fixed. No business-module endpoint or Audit Logs functionality was added. The transaction comment in the individual save method identifies the future audit integration point without implementing it.
