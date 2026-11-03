# How to Use This Repository & Documentation Guide

**Project:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Target Audience:** All 10 Team Members  

Welcome to the WoodFlow repository! Everything in this project is organized into two folders (`database/` and `docs/`) so every member can build their assigned Full-Stack Vertical Slice (PostgreSQL 16 + ASP.NET Core + Angular) without getting blocked.

---

## Step-by-Step Navigation Guide for Team Members

### Step 1: Check Your Assigned Module & Responsibilities
👉 **Open:** [`docs/02_modular_architecture_and_workload_division.md`](02_modular_architecture_and_workload_division.md)  
* **What it is:** The master breakdown of all **10 Member Slices (Member 1 to Member 10)**.
* **How to use it:** Find your name and Member number. It lists:
  1. Your dedicated **PostgreSQL Tables, Stored Procedures, Triggers, or Views**.
  2. Your **ASP.NET Core Web API Controllers & Services** to create.
  3. Your **Angular Frontend Screens** to build.
  4. Your **Fail-Safe Rule** (how your module works independently even if another teammate's module isn't finished yet).

---

### Step 2: Set Up Your Local PostgreSQL 16 Database
👉 **Open:** [`database/team_database_setup_guide.md`](../database/team_database_setup_guide.md)  
* **What it is:** Step-by-step guide to setting up **PostgreSQL 16 locally** (using **pgAdmin 4** or **Docker**).
* **How to use it:**
  1. Create a local database named `woodflow_db` in **pgAdmin 4** (or run `docker compose up -d`).
  2. Open [`database/schema.sql`](../database/schema.sql), copy everything, and execute it in pgAdmin 4 (`F5`) to create all 16 tables, triggers, stored procedures, views, and sample data locally.
  3. Reference [`database/schema.sql`](../database/schema.sql) to see the exact column names, data types, foreign keys, triggers, and stored procedures for your tables.

---

### Step 3: Understand the Business Workflow & Database Design
👉 **Open:**
* [`docs/01_business_context.md`](01_business_context.md) — Explains how Wimalasiri Furniture in Kottawa actually operates (Showroom retail vs. 1/3 deposit custom orders, the 6 workshop stages, and artisan payroll).
* [`docs/03_database_design_and_eer.md`](03_database_design_and_eer.md) — Explains the 3NF normalization, EER diagram (`docs/assets/eer_diagram.png`), and advanced ADBMS features required for our university viva marks.
* [`docs/04_product_requirements_document_prd.md`](04_product_requirements_document_prd.md) — Detailed functional requirements and validation rules for each screen.

---

### Step 4: Build Your Backend API Endpoints
👉 **Open:** [`docs/05_api_contract_specification.md`](05_api_contract_specification.md)  
* **What it is:** The REST API routes (`GET`, `POST`, `PUT`, `PATCH`) and JSON request/response payloads for each module.
* **How to use it:** Use these exact URL routes and JSON property names when creating your ASP.NET Core controllers so the Angular frontend and backend match cleanly.

---

### Step 5: Reference the UI Wireframes (Initial Prototype Only)
👉 **Open:** [`docs/ui_wireframes_ash_blue.html`](ui_wireframes_ash_blue.html) *(Double-click this file on your computer to open it in Chrome/Edge)*  
* **What it is:** An interactive HTML layout showing the **Ash & Blue** color theme and sample screen layouts.
* **IMPORTANT NOTE ON UI:** **This wireframe file is ONLY an initial visual concept/reference — it is NOT the final UI!** The layout, components, and fields **will change and improve** as each member develops their actual Angular screens.

---

## Rules When Updating the Database During Development
1. **ADBMS Requirement:** Every member must implement and demonstrate Stored Procedures, Functions, Triggers, or Views for their module in local PostgreSQL.
2. **Safe Schema Updates:** If you need a new column or procedure while coding, use `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` or `CREATE OR REPLACE` (never `DROP` existing tables/columns without asking the team).
3. **Keep `database/schema.sql` Updated:** Whenever you add a new column, trigger, view, or stored procedure in your local pgAdmin 4, also paste it into [`database/schema.sql`](../database/schema.sql) and commit it to your Git branch so the whole team stays in sync.
