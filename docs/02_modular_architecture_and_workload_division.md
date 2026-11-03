# Modular Architecture & 10-Member Vertical Slice Workload Division
**Project:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Architecture Pattern:** Modular Monolith / Vertical Slice Architecture  
**Academic Target:** CS304.3 Advanced Database Management Systems (Batch 25.1)  
**Team Composition:** 10 Students (Each owning a Full-Stack Slice: DB + Backend API + Frontend UI)

---

## 1. The 10-Member Engineering Strategy: Complete Isolation & Fail-Safe Protection
In a 10-member university software engineering project, horizontal layering (*e.g., 3 people doing DB, 4 doing backend, 3 doing frontend*) almost always leads to coordination deadlocks and unequal grading marks.

### Our Strategy: 10 Independent Full-Stack Vertical Slices
* **Every single student** designs their own database tables, writes their own stored procedures/triggers, develops their own ASP.NET Core API controllers, and creates their own Angular UI components.
* **Loose Coupling via Shared Contracts:** If any member falls behind or fails to deliver, **the other 9 modules continue to function and demo seamlessly** without crashing, using nullable foreign keys and mock service fallbacks.

```
                            ┌─────────────────────────────────────────────────────────┐
                            │               SHARED CORE FOUNDATION (All)              │
                            │      PostgreSQL DbContext, ASP.NET JWT Auth, Angular    │
                            └────────────────────────────┬────────────────────────────┘
                                                         │
    ┌──────────┬──────────┬──────────┬──────────┬────────┴─┬──────────┬──────────┬──────────┬──────────┐
    ▼          ▼          ▼          ▼          ▼          ▼          ▼          ▼          ▼          ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│Member 1│ │Member 2│ │Member 3│ │Member 4│ │Member 5│ │Member 6│ │Member 7│ │Member 8│ │Member 9│ │Mem. 10 │
│Showroom│ │Custom  │ │Workshop│ │Finishing│ │Finished│ │Raw     │ │Staff   │ │Artisan │ │Worker  │ │Exec.   │
│  POS   │ │Deposit │ │Carpentry││ & Deliv.│ │Goods Inv││Materials│ │Profiles│ │Piecework││Advances│ │Dashboard│
└────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘
```

---

## 2. Detailed 10-Member Full-Stack Allocations

### Member 1: Yohan Methusael E J (ID: 36999) — Showroom POS & Counter Billing
* **Database (PostgreSQL):** Tables: `showroom_invoices`, `showroom_invoice_items`, `cash_counter_transactions`. View: `vw_daily_counter_sales`.
* **Backend (ASP.NET Core):** `PosCounterController`, `CounterReceiptService`.
* **Frontend (Angular):** Fast barcode/item search POS counter, instant total calculation, cash drawer change calculator.
* **Fail-Safe Rule:** Sells finished furniture directly without needing custom workshop jobs or advance deposit logic.

### Member 2: M.T.R. Mathota (ID: 36231) — Custom Quotations & Advance Deposit Engine
* **Database (PostgreSQL):** Tables: `custom_quotations`, `custom_orders`, `order_deposits`. Stored Procedure: `sp_create_order_with_deposit` (validates mandatory 1/3 deposit rule).
* **Backend (ASP.NET Core):** `QuotationsController`, `CustomOrdersController`, deposit validation filters.
* **Frontend (Angular):** Custom quotation builder (wood type, dimensions, fabric swatches), 33.3% deposit calculator, order confirmation slip.
* **Fail-Safe Rule:** Generates formal custom order invoices independently; passes order ID to workshop via loose reference.

### Member 3: P Kukarakshan (ID: 37182) — Workshop Carpentry & Framework Tracker (Stages 1 & 2)
* **Database (PostgreSQL):** Tables: `production_jobs`, `stage_framework_logs`. Trigger: `trg_validate_framework_signoff`. Stage enum: `DESIGN_APPROVAL`, `TIMBER_CUTTING_FRAMEWORK`.
* **Backend (ASP.NET Core):** `WorkshopCarpentryController`, job ticket generation.
* **Frontend (Angular):** Workshop Board for Stages 1 & 2, timber requirement checklist, carpenter task assignment screen.
* **Fail-Safe Rule:** Can create and track physical carpentry tickets independently even if the sales POS is offline.

### Member 4: D. S. S. Nayakasena (ID: 36285) — Workshop Finishing & Delivery Tracker (Stages 3, 4, 5 & 6)
* **Database (PostgreSQL):** Tables: `stage_finishing_logs`, `delivery_schedules`. Stages: `CUSHIONING_UPHOLSTERY`, `SANDING_SPRAY_POLISHING`, `QUALITY_INSPECTION`, `DELIVERY`.
* **Backend (ASP.NET Core):** `WorkshopFinishingController`, `DeliveryController`.
* **Frontend (Angular):** Polishing & Cushioning Kanban columns, Quality Inspection checklist (pass/fail), Delivery lorry schedule & balance collection flag.
* **Fail-Safe Rule:** Reads job framework output; if earlier stages are delayed, can test with mock completed frameworks.

### Member 5: P Rejishanth (ID: 36791) — Finished Furniture Inventory (Showroom Stock)
* **Database (PostgreSQL):** Tables: `finished_furniture_items`, `showroom_categories`, `stock_adjustments`. Trigger: `trg_deduct_showroom_stock`.
* **Backend (ASP.NET Core):** `FinishedGoodsController`, retail price management, stock audit API.
* **Frontend (Angular):** Visual catalog of showroom furniture (beds, sofas, tables), stock count badges, price tag generator.
* **Fail-Safe Rule:** Completely standalone product catalog; provides `IFinishedStockService` for POS checkout.

### Member 6: Muhammed AT (ID: 37155) — Raw Materials & Workshop Supplies Inventory
* **Database (PostgreSQL):** Tables: `raw_materials` (Timber board-feet, foam sheets, fabric meters, varnish gallons, screws), `material_suppliers`, `purchase_receipts`. Trigger: `trg_check_low_raw_stock`.
* **Backend (ASP.NET Core):** `RawMaterialsController`, `SuppliersController`, reorder threshold monitor.
* **Frontend (Angular):** Raw material inventory dashboard, low-stock warning banners, supplier restock purchase entry.
* **Fail-Safe Rule:** Operates independently as the workshop stockroom; provides consumption endpoints to carpentry stages.

### Member 7: R.P.G.B Kumarathunga (ID: 36969) — Employee Profiles & Base Salary Payroll
* **Database (PostgreSQL):** Tables: `employees`, `attendance_records`, `monthly_base_salaries`. View: `vw_active_workforce_summary`.
* **Backend (ASP.NET Core):** `EmployeesController`, `AttendanceController`, `BaseSalaryController`.
* **Frontend (Angular):** Staff directory (owner, cashier, 5 craftsmen), daily counter/workshop attendance log, fixed monthly pay slip generator.
* **Fail-Safe Rule:** Core employee register; functions as the master identity provider for other labor modules.

### Member 8: G. D. S. Suwahas (ID: 32080) — Artisan Piecework & Job Labor Allocations
* **Database (PostgreSQL):** Tables: `artisan_job_rates`, `piecework_credits`. Stored Procedure: `sp_calculate_artisan_piecework_earnings`.
* **Backend (ASP.NET Core):** `PieceworkController`, labor cost calculation per furniture piece.
* **Frontend (Angular):** Job rate agreement matrix (e.g., Rs. 8,000 for table frame, Rs. 4,000 for polishing), artisan credit approval screen.
* **Fail-Safe Rule:** Links optionally to `job_id`; if workshop tracker is lagging, piecework can be logged directly against employee IDs.

### Member 9: SM Asfaq (ID: 37222) — Worker Cash Advances & Daily Cash Ledger
* **Database (PostgreSQL):** Tables: `worker_advances`, `petty_cash_ledger`. Stored Procedure: `sp_reconcile_daily_petty_cash`.
* **Backend (ASP.NET Core):** `AdvancesController`, `PettyCashController`, advance approval workflows.
* **Frontend (Angular):** Cash advance issuance form, worker outstanding advance balance cards, daily counter petty cash book.
* **Fail-Safe Rule:** Operates independently as an advance ledger; exposes `GetUnsettledAdvances(employeeId)` for month-end deductions.

### Member 10: H.G.Y.D.Chamaya (ID: 36794) — Executive Analytics & Automated WhatsApp Dispatch
* **Database (PostgreSQL):** Views: `vw_customer_outstanding_balances`, `vw_monthly_executive_summary`, `mat_vw_profit_loss_rollup`. Indexing optimization scripts with `EXPLAIN ANALYZE`.
* **Backend (ASP.NET Core):** `AnalyticsController`, `WhatsAppNotificationService` (Meta Cloud API / Click-to-Chat), `PdfReportExporter`.
* **Frontend (Angular):** Executive Owner Dashboard (Daily cash collected, debtor balances, workshop throughput, Net Profit & Loss graphs).
* **Fail-Safe Rule:** Reads analytical views; handles PDF generation and WhatsApp delivery without modifying underlying transactional schemas.

---

## 3. Workload Balance & Assessment Compliance
Every single student satisfies all requirements of the CS304.3 assessment:
1. **Database:** Has at least 2–3 dedicated tables, primary/foreign keys, and either an ACID transaction, stored procedure, trigger, or complex view.
2. **Backend:** Has dedicated ASP.NET Core controllers and business logic services.
3. **Frontend:** Has responsive Angular components styled in the corporate Ash & Blue palette.
4. **Resilience:** The failure or absence of one student's module will never crash the system during the lecturer demonstration.
