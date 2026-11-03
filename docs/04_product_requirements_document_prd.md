# Product Requirements Document (PRD) — 10 Member Slices
**System:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Architecture:** Modular Vertical Slices (Angular 17 + ASP.NET Core 8 + PostgreSQL 16)  
**Academic Target:** CS304.3 Advanced Database Management Systems

---

## 1. Document Purpose & Team Governance
This PRD defines the exact functional scope, business validation rules, and grading acceptance criteria for each of the **10 student modules**. 
* Every student owns a complete **Full-Stack Vertical Slice** (Database Schema + Stored Proc/Trigger + ASP.NET API Controller + Angular UI Component).
* Each module includes explicit **Decoupled Fallback Rules** so that if a teammate delays, other modules continue to build, run, and demonstrate without failure.

---

## 2. The 10 Member Slice Specifications

### Member 1: Showroom POS & Counter Invoicing
* **Role Title:** Retail POS & Billing Lead
* **User Stories:**
  * *As a Showroom Cashier*, I want to quickly search finished furniture by SKU or name and add them to a sales cart so that walk-in customers can be billed in under 30 seconds.
  * *As a Cashier*, I want the system to calculate subtotal, discounts, and change due so that manual math errors are eliminated.
* **Database Objects Owned:** `showroom_invoices`, `showroom_invoice_items`, `trg_pos_stock_deduct`.
* **Business & Validation Rules:**
  1. Cart must contain at least 1 item with quantity $\ge 1$.
  2. Stock deduction is automated via database trigger (`trg_pos_stock_deduct`). If available stock $< \text{requested quantity}$, the transaction must abort with an error.
  3. Payment mode must be `CASH`, `CARD`, or `BANK_TRANSFER`.
* **Acceptance Criteria (Grading Proof):**
  * Adding 1 King Bed to invoice decrements `finished_furniture.current_stock` by 1 immediately.
  * A printable counter receipt is generated with invoice number, date, cashier name, and line items.

---

### Member 2: Custom Quotations & 1/3 Advance Deposit Engine
* **Role Title:** Custom Invoicing & Advance Deposit Lead
* **User Stories:**
  * *As the Shop Owner*, I want to record bespoke furniture inquiries with agreed pricing and automatically compute the mandatory one-third (33.3%) advance deposit.
  * *As the Owner*, I want the system to block order confirmation until the 1/3 deposit is paid so that Wimalasiri Furniture never buys timber without financial commitment.
* **Database Objects Owned:** `custom_orders`, `order_deposits`, `sp_create_order_with_deposit`.
* **Business & Validation Rules:**
  1. Minimum deposit is enforced inside PostgreSQL stored procedure:
     $$\text{Min Deposit} = \text{ROUND}(\text{Agreed Price} / 3.0, 2)$$
  2. If deposit entered is less than the 33.3% floor, PostgreSQL raises a business exception: *"Deposit below mandatory 1/3 rule"*.
  3. Upon successful deposit, order status changes to `CONFIRMED` and auto-spawns a linked `production_jobs` ticket.
* **Acceptance Criteria (Grading Proof):**
  * Demonstrating entering Rs. 100,000 agreed price with Rs. 30,000 deposit causes a validation error; entering Rs. 35,000 succeeds and logs into `order_deposits`.

---

### Member 3: Workshop Carpentry & Framework Tracker (Stages 1 & 2)
* **Role Title:** Workshop Framework Lead
* **User Stories:**
  * *As the Workshop Supervisor*, I want to see custom orders that need design approval and timber framework cutting so that carpenters know what to build today.
  * *As a Lead Carpenter*, I want to mark when the wooden frame is completed so the piece can move to upholstery or polishing.
* **Database Objects Owned:** `production_jobs` (Stages 1 & 2 fields), `framework_logs`.
* **Business & Validation Rules:**
  1. Job cannot move to Stage 2 (`TIMBER_CUTTING_FRAMEWORK`) until `design_approved = TRUE`.
  2. Lead carpenter ID must be assigned before framework completion can be logged.
* **Acceptance Criteria (Grading Proof):**
  * Workshop Kanban displays columns for *Stage 1: Design Approval* and *Stage 2: Timber Cutting & Framework*. Moving a job updates timestamp in `framework_logs`.

---

### Member 4: Workshop Finishing & Delivery Tracker (Stages 3, 4, 5 & 6)
* **Role Title:** Workshop Finishing & Logistics Lead
* **User Stories:**
  * *As an Upholsterer / Polisher*, I want to log when cushioning and spray polishing are done so that the item is scheduled for inspection.
  * *As the Owner*, I want a Quality Inspection sign-off before loading onto the delivery lorry so customers never receive defective furniture.
* **Database Objects Owned:** `finishing_logs`, `delivery_schedules`.
* **Business & Validation Rules:**
  1. Job cannot advance to Stage 6 (`DELIVERY`) unless Stage 5 (`QUALITY_INSPECTION`) is marked `quality_passed = TRUE`.
  2. Delivery record must capture vehicle lorry number and remaining balance to collect at the customer's door.
* **Acceptance Criteria (Grading Proof):**
  * Quality check form provides Pass/Fail buttons with notes. Passing the inspection marks the order `READY_FOR_DELIVERY` and generates the driver dispatch slip.

---

### Member 5: Finished Furniture Inventory Master
* **Role Title:** Showroom Inventory Lead
* **User Stories:**
  * *As the Showroom Clerk*, I want to catalog finished furniture with photos, SKU codes, dimensions, and retail selling prices.
  * *As the Owner*, I want a stock movement ledger to track when completed workshop items are moved into the showroom.
* **Database Objects Owned:** `finished_furniture`, `showroom_stock_movements`.
* **Business & Validation Rules:**
  1. SKU code must be unique (e.g., `FURN-BED-001`).
  2. Retail price must be greater than zero.
  3. Stock movements must record `stock_before`, `quantity_changed`, and `stock_after`.
* **Acceptance Criteria (Grading Proof):**
  * Showroom stock management grid allows adding new furniture pieces, updating prices, and displays live stock counts.

---

### Member 6: Raw Materials & Workshop Supplies Inventory
* **Role Title:** Raw Material & Procurement Lead
* **User Stories:**
  * *As the Workshop Foreman*, I want to track raw timber (board-feet), foam sheets, fabric meters, and varnish gallons so we never run out mid-job.
  * *As the Owner*, I want low-stock alert warnings when materials drop below their safety threshold so I can reorder from timber mills.
* **Database Objects Owned:** `raw_materials`, `job_materials`.
* **Business & Validation Rules:**
  1. Unit of measure must adhere to physical standards: `BOARD_FEET`, `METERS`, `GALLONS`, or `BOXES`.
  2. If `current_stock <= reorder_level`, UI displays a prominent warning badge.
* **Acceptance Criteria (Grading Proof):**
  * Decrementing Teak timber below 25 board-feet triggers a `LOW STOCK` visual badge in the raw inventory dashboard.

---

### Member 7: Employee Profiles & Base Salary Payroll
* **Role Title:** Workforce & Attendance Lead
* **User Stories:**
  * *As the Owner*, I want to maintain employee records (NIC, phone, employment type: Monthly, Piecework, or Hybrid).
  * *As the Owner*, I want to mark daily attendance (Present/Absent) and generate standard monthly base salary vouchers.
* **Database Objects Owned:** `employees`, `attendance_records`.
* **Business & Validation Rules:**
  1. Unique NIC validation for each employee.
  2. Salaried employees have fixed `base_monthly_salary`; piecework-only workers have base salary = Rs. 0.
* **Acceptance Criteria (Grading Proof):**
  * Employee directory displays staff cards with attendance status and standard base pay records.

---

### Member 8: Artisan Piecework & Job Labor Allocations
* **Role Title:** Artisan Piecework Accounting Lead
* **User Stories:**
  * *As the Workshop Foreman*, I want to allocate agreed labor fees to specific craftsmen per furniture piece (e.g., Rs. 8,000 for dining table frame).
  * *As a Carpenter*, I want my completed jobs credited to my monthly piecework balance once the supervisor approves the workmanship.
* **Database Objects Owned:** `piecework_credits`.
* **Business & Validation Rules:**
  1. Labor fee must be $> 0$.
  2. Piecework credit must be approved (`is_approved = TRUE`) before it is eligible for payroll settlement.
* **Acceptance Criteria (Grading Proof):**
  * Piecework credit matrix shows jobs completed by craftsmen; clicking "Approve" adds the amount to their unbilled earnings pool.

---

### Member 9: Worker Cash Advances & Daily Petty Cash Ledger
* **Role Title:** Worker Advances & Cash Book Lead
* **User Stories:**
  * *As a Craftsman*, I want to request a mid-month cash advance (e.g., Rs. 5,000) for family emergencies.
  * *As the Owner*, I want to record cash advances so they are automatically deducted at month-end, preventing unrecovered loans.
* **Database Objects Owned:** `worker_advances`.
* **Business & Validation Rules:**
  1. Advance amount must be $> 0$.
  2. An advance starts with `is_settled = FALSE` and switches to `TRUE` only when the monthly payroll settlement runs.
* **Acceptance Criteria (Grading Proof):**
  * Issuing Rs. 5,000 advance logs into `worker_advances` and displays on the employee's active balance card.

---

### Member 10: Executive Analytics & Automated WhatsApp Dispatch
* **Role Title:** Executive Business Intelligence & Messaging Lead
* **User Stories:**
  * *As the Business Owner*, I want a real-time dashboard showing today's cash collections, total customer credit balances, and net monthly profit so I can make informed business decisions.
  * *As a Customer*, I want an official PDF receipt delivered directly to my WhatsApp upon paying my deposit.
* **Database Objects Owned:** `vw_customer_outstanding_balances`, `monthly_payroll_settlements`.
* **Business & Validation Rules:**
  1. Customer outstanding balance view computes:
     $$\text{Balance Due} = \sum(\text{Agreed Price}) - \sum(\text{Deposits Paid})$$
  2. WhatsApp link generator creates formatted `https://wa.me/{number}?text={encoded_message}` or triggers Meta Cloud API.
* **Acceptance Criteria (Grading Proof):**
  * Executive dashboard renders KPI cards with live SQL aggregations; clicking "WhatsApp Receipt" opens WhatsApp with customer pre-filled receipt details.

---

## 3. Team Acceptance Checklist (Before Submission)
1. **Database:** All 10 members can run `schema.sql` locally via Docker or pgAdmin.
2. **Backend API:** Each member has tested their respective controller in Swagger UI (`/swagger`).
3. **Frontend UI:** Each member has connected their Angular screen to their backend endpoints.
4. **Resilience Test:** Disabling any single module must not prevent the other 9 modules from loading or demonstrating to the lecturer.
