# WoodFlow — Showroom Retail, Custom Workshop & Workforce Management System

**Client:** Wimalasiri Furniture, High Level Road, Kottawa  
**Module:** CS304.3 — Advanced Database Management Systems (ADBMS)  
**Tech Stack:** Angular (Standalone UI) • ASP.NET Core 8 Web API • PostgreSQL 16 (Local RDBMS via pgAdmin 4 / Docker)

---

## Repository Structure

```text
├── database/
│   ├── schema.sql                         # Master PostgreSQL 16 schema (16 tables, SPs, triggers, views, seed data)
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

### 1. Clone the Repository & Create Your Feature Branch
Never commit directly to `main`. Create a feature branch for your assigned module:
```bash
git clone https://github.com/Methu25/woodflow-adbms.git
cd woodflow-adbms
git checkout -b feature/member-XX-module-name
```

### 2. Set Up Your Local PostgreSQL 16 Database
Read **[`database/team_database_setup_guide.md`](database/team_database_setup_guide.md)** for full instructions:
- **Option 1 (pgAdmin 4):** Create a local database named `woodflow_db` in **pgAdmin 4**, open the Query Tool, and execute [`database/schema.sql`](database/schema.sql).
- **Option 2 (Docker):** Run `docker compose up -d` in the project root folder to start a local PostgreSQL 16 container pre-loaded with [`database/schema.sql`](database/schema.sql).
- **Local Connection String (`appsettings.json`):**
  ```json
  {
    "ConnectionStrings": {
      "WoodFlowDb": "Host=localhost;Port=5432;Database=woodflow_db;Username=postgres;Password=password123"
    }
  }
  ```

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
