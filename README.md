# WoodFlow — Showroom Retail, Custom Workshop & Workforce Management System

**Client:** Wimalasiri Furniture, High Level Road, Kottawa  
**Module:** CS304.3 — Advanced Database Management Systems (ADBMS)  
**Tech Stack:** Angular 18 (Standalone UI) • ASP.NET Core 10 Web API • PostgreSQL 16 (Local RDBMS via pgAdmin 4 / Docker)

---

## Repository Structure

```text
├── database/
│   ├── schema.sql                         # Consolidated PostgreSQL 16 schema through migration 007
│   └── team_database_setup_guide.md       # Step-by-step local PostgreSQL 16 setup guide (pgAdmin 4 & Docker)
├── docs/
│   ├── 00_HOW_TO_USE_THESE_DOCS.md        # START HERE: Guide on how to navigate & use all project files
│   ├── CS304_3_Final_Project_Proposal_WoodFlow.docx  # Finalized Project Proposal Report
│   ├── 01_business_context.md             # Business domain & Kottawa showroom/workshop workflows
│   ├── 02_modular_architecture_and_workload_division.md # 10-Member Vertical Slice Allocation
│   ├── 03_database_design_and_eer.md      # 3NF Relational Schema, EER & Advanced SQL Specifications
│   ├── 04_product_requirements_document_prd.md       # Product Requirements Document (PRD)
│   ├── 05_api_contract_specification.md   # REST API Endpoints & JSON Contracts
│   ├── ui_wireframes_ash_blue.html        # Initial UI Wireframes Prototype (will evolve during coding)
│   └── assets/                            # EER & System Architecture Diagrams
└── docker-compose.yml                     # Optional 1-command local PostgreSQL 16 container
```

---

## Quick Start for Team Members

**Start with [TEAM_DEVELOPMENT_SETUP.md](TEAM_DEVELOPMENT_SETUP.md).** It is the
authoritative setup, security ownership, design and pre-push safety guide.
The notes below are a short summary; linked feature guides provide API details.

### 1. Clone the Repository & Create Your Feature Branch
Never commit directly to `main`. Create a feature branch for your assigned module:
```bash
git clone https://github.com/Methu25/woodflow-adbms.git
cd woodflow-adbms
git checkout -b feature/member-XX-module-name
```

### 2. Initialize your local database

Follow [database/team_database_setup_guide.md](database/team_database_setup_guide.md).
For a NEW empty PostgreSQL 16 database, apply `database/schema.sql` once only;
it includes migrations 001–007. Existing databases use outstanding migrations
in order instead. Never rerun the schema on a working database.
Optional Docker requires your own password in ignored `.env` (see `.env.example`);
its existing named volume is preserved. Native PostgreSQL and Docker cannot both
bind the same port.

### 3. Restore and start the applications

Install .NET 10 SDK, Node.js 22 and npm 10+. Source projects and tests are in
`backend/`; Angular source, local assets and startup scripts are in `frontend/`;
upgrade scripts remain in `database/migrations/`.

```powershell
dotnet restore backend/WoodFlow.slnx
dotnet build backend/WoodFlow.slnx
npm.cmd --prefix frontend ci
```

[backend/README.md](backend/README.md) explains private database configuration and
starting the API at **https://localhost:7001**.
[frontend/README.md](frontend/README.md) explains generating/trusting each laptop's
own development certificate and `npm start` at **https://localhost:4200**.
Follow [backend/AUTH_SETUP.md](backend/AUTH_SETUP.md) for your own first OWNER.
Never commit secrets, `.env`, private keys, local appsettings, generated folders
or database exports. Existing shared database credentials should be rotated.
Older proposal/design documents describe planned modules; current runtime setup
is documented in the linked backend/frontend guides.

---
## 10-Member Modular Workload Division

Full details: **[`docs/02_modular_architecture_and_workload_division.md`](docs/02_modular_architecture_and_workload_division.md)**

| Member | Student Name (ID) | Assigned Vertical Slice Module | Primary Database Tables & Advanced DB Objects |
| :--- | :--- | :--- | :--- |
| **Member 1** | **Yohan Methusael E J** (36999) | Showroom POS & Counter Billing | `showroom_invoices`, `showroom_invoice_items`, `trg_pos_stock_deduct` |
| **Member 2** | **M.T.R. Mathota** (36231) | Custom Quotations & 1/3 Deposit Engine | `custom_orders`, `order_deposits`, `sp_create_order_with_deposit` |
| **Member 3** | **P. Kukarakshan** (37182) | Workshop Carpentry & Framework Tracker (Stages 1–2) | `production_jobs`, `framework_logs`, `trg_log_stage_change` |
| **Member 4** | **D.S.S. Nayakasena** (36285) | Upholstery, Spray Polish, QC & Delivery (Stages 3–6) | `finishing_logs`, `production_jobs` |
| **Member 5** | **P. Rejishanth** (36791) | Finished Furniture & Showroom Stock Control | `finished_furniture`, `showroom_stock_movements` |
| **Member 6** | **Muhammed A.T.** (37155) | Timber Yard & Raw Materials Inventory | `raw_materials`, `job_materials` |
| **Member 7** | **R.P.G.B. Kumarathunga** (36969) | Employee Directory & Daily Attendance | `employees`, `attendance_records` |
| **Member 8** | **G.D.S. Suwahas** (32080) | Artisan Piecework & Labor Credit Engine | `piecework_credits` |
| **Member 9** | **S.M. Asfaq** (37222) | Worker Cash Advances, Petty Cash & Monthly Payroll | `worker_advances`, `petty_cash_ledger`, `monthly_payroll_settlements`, `sp_settle_monthly_payroll` |
| **Member 10** | **H.G.Y.D. Chamaya** (36794) | Owner Executive Analytics & Audit Center | `audit_logs`, `vw_customer_outstanding_balances`, `mat_vw_monthly_profit_summary` |
