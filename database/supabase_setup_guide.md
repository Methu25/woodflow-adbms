# Supabase Cloud Database Setup Guide
**Project:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Target:** Unified Cloud PostgreSQL Database for All 10 Team Members  
**Prerequisites:** A web browser and a GitHub or Google account (Zero local installations required)

---

## Why Supabase Instead of Firebase? (Important for CS304.3 ADBMS)

| Feature / Assessment Criteria | Firebase (Firestore NoSQL) | Supabase (Cloud PostgreSQL) |
| :--- | :--- | :--- |
| **Database Engine** | NoSQL Document Store (JSON) | **Enterprise PostgreSQL 16 (Relational RDBMS)** |
| **CS304.3 ADBMS Syllabus Fit** | ❌ Fails (NoSQL cannot fulfill relational requirements) | **✅ 100% Compliant with module grading criteria** |
| **3NF Normalization & Foreign Keys** | ❌ None (De-normalized collections) | **✅ Full Primary Keys, Foreign Keys, ON DELETE CASCADE** |
| **Stored Procedures & Functions** | ❌ Not supported in database engine | **✅ Full PL/pgSQL support (`sp_create_order_with_deposit`, etc.)** |
| **Database Triggers** | ❌ Cloud Functions only (Node.js, not DB triggers) | **✅ Native SQL triggers (`trg_pos_stock_deduct`, low-stock alerts)** |
| **Complex Relational Joins & Views** | ❌ No native joins (multiple client queries needed) | **✅ Materialized views, indexed views, multi-table `JOIN`s** |
| **ACID Multi-Table Transactions** | ❌ Limited document transactions | **✅ Full ACID atomic transactions with rollback** |
| **Team Collaboration** | Cloud-based | **Cloud-based with unified connection string for all 10 members** |
| **Web Dashboard** | Yes (Firebase Console) | **Yes (Supabase Studio Table Editor & SQL runner)** |

> **Summary:** Supabase gives you all the advantages of Firebase (free cloud hosting, live web dashboard, zero local database install) while running **pure PostgreSQL** that satisfies every single requirement of your Advanced Database Management Systems module.

---

## Step-by-Step Setup Guide (Takes Under 3 Minutes)

### Step 1: Create a Free Account
1. Visit **[https://supabase.com](https://supabase.com)**.
2. Click **Start your project** and sign in using your **GitHub** or **Google** account.

---

### Step 2: Create a New Database Project
1. On the Supabase Dashboard, click **New project**.
2. Fill in the project details:
   - **Name:** `woodflow-db`
   - **Database Password:** Enter a strong password and keep note of it privately.
   - **Region:** Select `Southeast Asia (Singapore)` or `Northeast Asia (Tokyo)` for low latency.
   - **Pricing Plan:** Select **Free ($0/month)**.
3. Click **Create new project**. Supabase will provision your database in about 60–90 seconds.

---

### Step 3: Run `schema.sql` to Create All Tables & Procedures
1. In your Supabase project dashboard, click on the **SQL Editor** tab (icon looking like `>_` on the left sidebar).
2. Click **New query** (or `+`).
3. Open our project file [`database/schema.sql`](schema.sql) in your code editor or notepad.
4. Copy the entire file content (`Ctrl + A`, then `Ctrl + C`).
5. Paste it into the Supabase SQL Editor window (`Ctrl + V`).
6. Click the green **Run** button (or press `Ctrl + Enter`).
7. You will see:
   ```text
   Success. No rows returned.
   ```
   All 16 tables, 3 triggers, 2 stored procedures, indexed views, and realistic sample furniture rows are now live!

---

### Step 4: Verify the Tables in Supabase Table Editor
1. Click the **Table Editor** icon (grid icon on the left sidebar).
2. You will see all 16 tables:
   - `finished_furniture` (pre-populated with King Size Teak Bed, Dining Table, Wardrobe)
   - `custom_orders`
   - `production_jobs`
   - `raw_materials`
   - `employees`
   - `worker_advances`
   - ... and all other tables.
3. You can click on any table to view, edit, or insert records directly from your web browser, exactly like a spreadsheet or Firebase console.

---

### Step 5: Get the IPv4 Session Pooler Connection String for Your 10 Team Members
1. At the top of your Supabase Dashboard, click **Connect**.
2. Under **Connection Method**, select **Session pooler** (supports IPv4 networks across Sri Lanka for free).
3. Copy the URI or Npgsql connection string:
   - **URI format:**
     ```text
     postgresql://postgres.[PROJECT-REF]:[YOUR-PASSWORD]@aws-0-ap-northeast-1.pooler.supabase.com:5432/postgres
     ```
   - Replace `[PROJECT-REF]` and `[YOUR-PASSWORD]` with the credentials shared privately in the team group.

---

### Step 6: Configure ASP.NET Core (`appsettings.secrets.json`)
Every team member places the `appsettings.secrets.json` file (shared privately via WhatsApp) into their project root:

```json
{
  "ConnectionStrings": {
    "WoodFlowDb": "Host=aws-0-ap-northeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.[PROJECT-REF];Password=[YOUR-PASSWORD];SSL Mode=Require;Trust Server Certificate=true;"
  }
}
```

---

## Benefits for Your 10-Member Group
1. **Zero Database Installation:** Teammates do not need to install Docker, WSL2, PostgreSQL, or pgAdmin on their computers.
2. **Always in Sync:** When Member 1 tests POS sales, Member 5 immediately sees showroom stock decrease in real time.
3. **No Database Drift:** Everyone tests against the exact same stored procedures, triggers, and seed data.
4. **Emergency Recovery:** If a teammate accidentally corrupts their local code, the cloud database remains safe and untouched.
