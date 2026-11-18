# WoodFlow frontend

Angular 18 standalone application with SCSS and Angular Router. Requires Node.js
22, npm, and the .NET 10 SDK for local certificates/backend. Use npm.cmd in
PowerShell if script execution policy blocks npm.ps1.

## Local HTTPS setup (once per developer)

From the repository root, trust your own localhost development certificate and
export it for Angular:

```powershell
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force frontend/.certs | Out-Null
dotnet dev-certs https --export-path frontend/.certs/localhost.pem --format PEM --no-password
```

This creates localhost.pem and localhost.key. They are ignored by git. The key
has no export password because the local development server must read it; keep
it private and never commit, upload, or share either file. Each developer exports
their own certificate. Do not reuse a teammate's private key.

See the [official .NET certificate command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs)
for trust/export behavior. Re-export after replacing/renewing the backend
development certificate. Use the same local certificate for both processes.

First configure your local woodflow_db connection and initialize its database using the database guide,
and initialize your own OWNER following [backend/AUTH_SETUP.md](../backend/AUTH_SETUP.md).
The frontend never generates setup authorizations or changes database schema.

## Start both applications

Terminal 1, from the repository root, with ConnectionStrings__WoodFlowDb configured:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/WoodFlow.Api --configuration Release --no-launch-profile -- --urls https://localhost:7001
```

Terminal 2, from the repository root:

```powershell
cd frontend
npm ci
npm start
```

Open **https://localhost:4200**. If that port is occupied, stop your old frontend
or use `npm start -- --port 4300`, then open https://localhost:4300.

angular.json enables HTTPS using .certs/localhost.pem and .certs/localhost.key.
The start script fails with setup instructions if either file is missing.
It supplies NODE_EXTRA_CA_CERTS to the Angular process so the proxy trusts the
exported .NET certificate. proxy.conf.json forwards /api to
https://localhost:7001 with **secure: true**. Do not change it to HTTP or disable
certificate verification to work around certificate errors.

All application requests use the environment's relative /api URL. The browser
sees one HTTPS origin, so Secure cookies and Angular XSRF work without CORS.
The backend must also be HTTPS; the old HTTP health-only listener is unsuitable
for authentication. Future hosting must serve the frontend over HTTPS and route
/api to the backend on the same origin. Frontend environment values are public;
never put credentials or setup codes in environment files.

## Screens and authentication

Password recovery adds `/forgot-password` and `/reset-password`, plus the OWNER-only
`/users/password-recovery` inbox. The existing Password Reset Requests tool in Users
& Roles opens this inbox and shows the logged-in owner's real pending count. Counts
refresh on entry, explicit Refresh, and approval/denial; there is no polling loop.
The inbox has three owner-work categories: Pending, Completed, and Denied. Completed
includes issued codes, finished resets, and expired requests, with the real outcome
shown on each row. Issuing a code completes the owner's task, not the password reset;
the backend still enforces approval, expiry, and single-use consumption separately.
For existing databases, apply only outstanding migrations; fresh schema includes 001–007. Start the backend as described in
[PASSWORD_RECOVERY.md](../backend/PASSWORD_RECOVERY.md) before live testing.

The owner must verify identity outside the app and explicitly confirm approval.
The returned code appears once, with its recipient and expiration time. Copy it
privately before closing: closing, navigating away, or hiding the page clears it.
Clipboard contents are not automatically erased. No codes/passwords are stored in
browser storage, URLs, or logs. Code retrieval and automatic delivery do not exist.
Reset uses the code alone; the backend does not need a username/request reference.
On success the form clears secrets, shows an OK dialog, and returns to login.

For isolated browser verification, start Angular on HTTPS port 4300, then in another
terminal from `frontend/` (requires a local Chrome/Chromium browser):

```powershell
npm.cmd run build
npm.cmd run test:auth
$env:WOODFLOW_TEST_BASE_URL = 'https://localhost:4300'
npm.cmd run test:recovery:browser
```

Set `BROWSER_PATH` if Chrome is installed elsewhere. The recovery browser test
intercepts **all** `/api` calls, verifies XSRF headers and UI behavior, and writes
ignored screenshots to `.angular/verification/`. Its reset/login/session results
are simulations, not proof of live PostgreSQL persistence or cookie revocation.
Live end-to-end testing requires explicitly approved test accounts/database changes.

- / and /splash: WoodFlow branding with a Start WoodFlow button. The splash
  remains visible until the user chooses to start; there is no automatic timer.
- /loading: starts GET /api/auth/me immediately and stays visible for at least
  3 seconds before successful navigation; verified users go to /dashboard, 401 goes to
  /login, and outages show a retry state instead of assuming a fresh installation.
  The bar fills steadily with a visible startup-readiness percentage: 90% represents
  the display phase and 10% represents the completed session check. It reaches
  100% only when both finish; it is not a network-download counter.
  Loading to login uses a gentle 1.2-second crossfade in browsers supporting
  View Transitions; reduced-motion preferences disable the crossfade.
- /login: validated username/password, password visibility toggle, pending state,
  safe errors, and no public registration or initial-setup link.
- /initial-setup: trusted installer manually opens this route and enters the
  server-generated one-time code, full name, username, password and confirmation.
  Passwords require 12–128 characters; usernames use 3–50 letters/digits/_.-.
  Successful setup goes to login and never signs in automatically.
- /dashboard: existing page preserved, protected by an authentication guard.
  The existing shell adds current-user information and a logout action.

AuthService keeps only safe account details in memory: userId, username, fullName
and OWNER/ASSISTANT role. It never stores credentials/codes in localStorage,
sessionStorage, URLs, or logs, and never reads HttpOnly authentication cookies.

provideHttpClient configures XSRF-TOKEN and X-XSRF-TOKEN. Every login, setup,
and logout obtains /api/auth/csrf before POST. Login/logout also refresh CSRF
after the identity change; a later mutation always refreshes again, even if that
post-success refresh failed. Concurrent session checks share one request, while
later protected navigation checks the backend again. Requests time out after
10 seconds; submissions cannot be duplicated while pending.

Backend authorization remains mandatory. Guards only guide navigation. A missing
cookie, browser change, or 401 never authorizes first-owner setup. No installation
state is inferred from browser storage. The server alone validates setup codes,
expiration, single use and initialization status.

## Figma reference and assets

Primary reference: [ADBMS Project Group Y](https://www.figma.com/design/m6Xv0EYAWLoms9lk0mmVWG/ADBMS-Project-Group-Y?node-id=65-1657).

Inspected only these authentication/startup frames under the Thisari page:
- SCREEN 10 – SPLASH: 215:1551
- SCREEN 11 – SPLASH LOADING: 215:1560
- SCREEN 16 – LOG IN: 226:1551
- SCREEN 13 – CREATE OWNER ACCOUNT: 237:1551

Furniture backgrounds, Sinhala brand artwork and icons are downloaded locally
under public/assets/auth, using their source frame IDs. Inter is self-hosted
under public/assets/fonts with its SIL Open Font License. No temporary Figma
URLs or remote font services are used at runtime.

Functional differences reflect the actual backend: the recovery-key field is
replaced by the one-time setup code, no persistent-login checkbox or unsupported
password-recovery action is offered, and startup checks a real session instead
of displaying fake catalog synchronization progress. Shared auth layout and
password controls provide consistent responsive styling, labels and focus states.
The dashboard is not restyled.

Phone layouts use a single form column, 16px input text to avoid automatic iOS
focus zoom, larger touch targets, and safe-area padding. Splash/loading content
and footers use normal document flow on phones and short landscape screens so
they can scroll without overlapping. Workspace labels stack on narrow phones.

## Verification

```powershell
npm run build
npm run test:auth
```

test:auth runs focused service/guard tests with mocked HTTP using Node's test
runner and the existing Angular build toolchain; it makes no backend requests.

Optional isolated browser checks use a locally installed Chrome/Chromium
(no browser package is added). Start the HTTPS frontend on port 4300, then run:

```powershell
node tests/browser-auth.mjs
```

Set BROWSER_PATH if Chrome is elsewhere. WOODFLOW_TEST_BASE_URL can select
another local HTTPS frontend origin. All /api calls are intercepted with mock
responses: guards, XSRF headers, login/logout, rate-limit errors, duplicate-submit
prevention, setup validation/no automatic login, retry states and responsive
layouts are checked. Screenshots and the isolated browser profile go under
.angular/ and are ignored. These checks do not prove live authentication.

Before real use, manually verify your configured backend with your own local
OWNER: login, refresh a protected page, logout, invalid credentials, expiry and
rate-limit feedback. On a genuinely new local installation, generate a setup
authorization using the backend guide, manually open /initial-setup, complete
setup, then log in. Never post your code/password into chat or screenshots.
No real accounts are created by frontend tests.

## Dependency restore on Windows

Stop your running Angular server (Ctrl+C in its terminal) before running npm ci.
A running esbuild process can lock its executable and cause EPERM during restore.
Do not delete source or certificates to resolve this; stop the server and retry.
