# Team Local Database Setup Guide (PostgreSQL 16)
**System:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Target Audience:** All 10 Team Members  
**Database Engine:** PostgreSQL 16 (Local RDBMS via pgAdmin 4 or Docker)

---

## Which Local Setup Option Should You Use?

* **Option 1 (Recommended for All Members): Standard PostgreSQL 16 + pgAdmin 4 Installer**
  * Uses the official PostgreSQL 16 Windows installer and **pgAdmin 4** GUI.
  * Best for our university ADBMS demonstration and individual viva (lets you visually show tables, stored procedures, triggers, EER diagrams, and `EXPLAIN ANALYZE` query plans offline).

* **Option 2 (For Docker Users): 1-Command Docker Setup**
  * Use this if you already have **Docker Desktop** installed on your laptop.

---

## Option 1 (Recommended): Standard Local PostgreSQL 16 + pgAdmin 4

### Step 1: Install PostgreSQL 16
1. Download **PostgreSQL 16 for Windows** (includes pgAdmin 4):  
   [https://www.postgresql.org/download/windows/](https://www.postgresql.org/download/windows/)
2. Run the installer:
   * Keep the default port: **`5432`**
   * Set the superuser (`postgres`) password to: **`password123`** (or your own password)

### Step 2: Create the Database & Run `schema.sql`
1. Open **pgAdmin 4** on your laptop.
2. In the left sidebar, expand **Servers** &rarr; **PostgreSQL 16**.
3. Right-click on **Databases** &rarr; **Create** &rarr; **Database...**
4. Enter Database name: **`woodflow_db`** and click **Save**.
5. Right-click on **`woodflow_db`** &rarr; select **Query Tool**.
6. In the Query Tool, click the **Open File** folder icon (or open [`database/schema.sql`](schema.sql) in VS Code, press `Ctrl + A` and `Ctrl + C`, and paste it into the Query Tool).
7. Click the **Execute / Run** button (play icon or press **`F5`**).
8. Refresh **Schemas &rarr; public &rarr; Tables** on the left sidebar — all **16 tables, 2 stored procedures, 2 triggers, views, and sample furniture records** are now live on your machine!

### Step 3: Configure Your ASP.NET Core Connection String
Place this connection string in your `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "ConnectionStrings": {
    "WoodFlowDb": "Host=localhost;Port=5432;Database=woodflow_db;Username=postgres;Password=password123"
  }
}
```
*(If you chose a different password during PostgreSQL installation, replace `password123` with your local password.)*

---

## Option 2: The 1-Command Docker Setup

If you have **Docker Desktop** installed and running:

1. Open PowerShell / Terminal in the project root folder:
   ```bash
   cd woodflow-adbms
   ```
2. Run:
   ```bash
   docker compose up -d
   ```
   Docker will automatically download PostgreSQL 16, start it on `localhost:5432` with database `woodflow_db` and password `password123`, and execute [`database/schema.sql`](schema.sql) automatically.

---

## How All 10 Members Keep Database Changes in Sync via GitHub

Since everyone runs PostgreSQL 16 locally on their laptop:
1. **Your Initial Database:** [`database/schema.sql`](schema.sql) already contains all 16 tables, foreign keys, core stored procedures, triggers, views, and seed data so every module works immediately.
2. **Adding Stored Procedures / Triggers / Columns for Your Module:**
   * Write and test your new SQL procedure, trigger, view, or `ALTER TABLE` in your local **pgAdmin 4 Query Tool**.
   * Copy that new SQL into [`database/schema.sql`](schema.sql) in your feature branch and commit it to GitHub.
   * When teammates pull the latest `main` branch, they simply re-run the updated section of [`database/schema.sql`](schema.sql) in pgAdmin 4!

---

## Verifying That Your Local Database Works

Run these test queries in your pgAdmin 4 Query Tool:
* `SELECT * FROM finished_furniture;` &rarr; Displays King Size Teak Bed, Dining Table, Wardrobe, Sofa.
* `SELECT * FROM customers;` &rarr; Displays sample Kottawa customers.
* `SELECT * FROM raw_materials;` &rarr; Displays timber planks, varnish, and velvet fabric.
