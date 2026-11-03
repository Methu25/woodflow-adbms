# Team Database Setup Guide (Simple & Beginner-Friendly)
**System:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Target Audience:** All 10 Team Members  
**Primary Recommendation:** Option 1 (Cloud Supabase) — Zero Installation Required!

---

## Which Option Should You Use?

* **Option 1 (Recommended for All 10 Members): Cloud Supabase Database**
  * **Zero software installation required** on your laptop (No Docker, no pgAdmin needed).
  * Everyone connects to the **same live database**; no database drift or missing tables.
  * Web interface lets you view and edit data like an online spreadsheet.
  * See full setup instructions: [`supabase_setup_guide.md`](supabase_setup_guide.md).

* **Option 2 (Offline Alternative): Direct Local pgAdmin Setup**
  * Use this if you want to work completely offline without internet access.
  * Lightweight native Windows installer.

* **Option 3 (Containerized): 1-Command Docker Setup**
  * Use this if you already use Docker Desktop on your machine.

---

## Option 1 (Recommended): Shared Cloud Database via Supabase

### Why We Use Supabase (And Not Firebase)
Your university module is **CS304.3 Advanced Database Management Systems (ADBMS)**.
* **Why not Firebase?** Firebase is a NoSQL document database. It does not support SQL stored procedures, triggers, 3NF relational normalization, foreign keys, or ACID multi-table transactions. Using Firebase would fail the grading rubric for this course!
* **Why Supabase?** Supabase provides the exact same cloud convenience as Firebase, but runs **enterprise PostgreSQL 16**. It runs all 16 relational tables, triggers, and stored procedures in our [`database/schema.sql`](schema.sql) file while letting all 10 members share one live URL.

### How to Connect (For Team Members)
Once the project lead creates the Supabase database and runs [`database/schema.sql`](schema.sql), each member only needs to paste the connection string into their ASP.NET Core `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "WoodFlowDb": "Host=aws-0-ap-northeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.[PROJECT-REF];Password=[YOUR-PASSWORD];SSL Mode=Require;Trust Server Certificate=true;"
  }
}
```

---

## Option 2: The Direct pgAdmin Setup (Offline Local Database)

If any member wants to develop offline without an internet connection:

1. Download and install standard **PostgreSQL 16 for Windows** (includes pgAdmin 4): [https://www.postgresql.org/download/windows/](https://www.postgresql.org/download/windows/)
2. Open **pgAdmin 4**.
3. Right-click on **Databases** &rarr; **Create** &rarr; **Database...**
4. Name the database: `woodflow_db` and click **Save**.
5. Right-click `woodflow_db` &rarr; click **Query Tool**.
6. Open [`database/schema.sql`](schema.sql), copy everything (`Ctrl + A`, `Ctrl + C`), paste into the Query Tool, and press **Execute (F5)**.
7. Set your local `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "WoodFlowDb": "Host=localhost;Port=5432;Database=woodflow_db;Username=postgres;Password=password123"
     }
   }
   ```

---

## Option 3: The 1-Command Docker Setup (For Docker Users)

If you already have **Docker Desktop** installed and running:

1. Open PowerShell in the project root directory:
   ```bash
   cd c:\Projects\adbms
   ```
2. Run:
   ```bash
   docker compose up -d
   ```
   Docker will automatically download PostgreSQL 16, start it on port `5432`, and run [`database/schema.sql`](schema.sql) to seed the database.

---

## Verifying That Your Connection Works

Regardless of which option you use, running the following test query will return the pre-populated seed data:
* `SELECT * FROM finished_furniture;` &rarr; Displays King Size Teak Bed, Dining Table, Wardrobe.
* `SELECT * FROM customers;` &rarr; Displays sample Kottawa customers.
* `SELECT * FROM raw_materials;` &rarr; Displays timber planks, varnish, and velvet fabric.
