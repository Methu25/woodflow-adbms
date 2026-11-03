# REST API Contract Specification — 10 Member Slices
**System:** WoodFlow — Wimalasiri Furniture, Kottawa  
**Base URL:** `http://localhost:5000/api/v1`  
**Authentication:** Bearer JWT Token (`Authorization: Bearer <token>`)  
**Format:** JSON (`Content-Type: application/json`)

---

## 1. Overview for Frontend & Backend Team Members
Each team member develops their controller in ASP.NET Core matching the contract below. Angular developers can immediately use these JSON request/response shapes to build mock services or real HTTP calls.

---

## 2. API Endpoints by Member

### Member 1: Showroom POS & Counter Billing
#### `POST /pos/invoices` — Create POS Sale
* **Request:**
  ```json
  {
    "customerId": 1,
    "paymentMode": "CASH",
    "items": [
      { "productId": 1, "quantity": 1, "unitPrice": 125000.00 }
    ]
  }
  ```
* **Response (201 Created):**
  ```json
  {
    "invoiceId": 101,
    "invoiceNumber": "INV-20261027-0101",
    "totalAmount": 125000.00,
    "paymentMode": "CASH",
    "status": "SUCCESS"
  }
  ```

---

### Member 2: Custom Quotations & 1/3 Deposit Engine
#### `POST /orders/custom` — Create Order with Minimum 1/3 Deposit
* **Request:**
  ```json
  {
    "customerId": 1,
    "furnitureDescription": "King Size Teak Bed with carved headboard",
    "agreedPrice": 120000.00,
    "depositAmount": 40000.00,
    "paymentMethod": "CASH"
  }
  ```
* **Response (201 Created):**
  ```json
  {
    "orderId": 45,
    "orderNumber": "ORD-20261027-4512",
    "agreedPrice": 120000.00,
    "minDepositRequired": 40000.00,
    "depositPaid": 40000.00,
    "remainingBalance": 80000.00,
    "orderStatus": "CONFIRMED",
    "linkedJobTicket": "JOB-2610-0045"
  }
  ```
* **Error Response (400 Bad Request — Deposit Violation):**
  ```json
  {
    "error": "DEPOSIT_BELOW_THRESHOLD",
    "message": "Deposit amount Rs. 30,000.00 is below the mandatory 1/3 deposit of Rs. 40,000.00"
  }
  ```

---

### Member 3: Workshop Carpentry & Framework (Stages 1 & 2)
#### `GET /workshop/framework/jobs` — Get Active Framework Jobs
* **Response (200 OK):**
  ```json
  [
    {
      "jobId": 12,
      "jobTicketNo": "JOB-2610-0012",
      "furnitureType": "Dining Table (6 Chairs)",
      "timberType": "Teak",
      "currentStage": "TIMBER_CUTTING_FRAMEWORK",
      "designApproved": true,
      "timberCutDate": "2026-10-25T08:30:00Z"
    }
  ]
  ```
#### `PATCH /workshop/framework/{jobId}/complete` — Mark Framework Completed
* **Request:**
  ```json
  { "leadCarpenterId": 1, "frameworkCompletedDate": "2026-10-27T10:00:00Z" }
  ```
* **Response (200 OK):**
  ```json
  { "jobId": 12, "nextStage": "CUSHIONING_UPHOLSTERY", "status": "FRAMEWORK_APPROVED" }
  ```

---

### Member 4: Workshop Finishing & Delivery (Stages 3 to 6)
#### `PATCH /workshop/finishing/{jobId}/quality-inspection` — Quality Check Pass
* **Request:**
  ```json
  { "qualityPassed": true, "inspectionNotes": "Sanding smooth, spray gloss uniform, hardware secure" }
  ```
* **Response (200 OK):**
  ```json
  { "jobId": 12, "currentStage": "DELIVERY", "status": "READY_FOR_DELIVERY" }
  ```
#### `POST /workshop/delivery/dispatch` — Dispatch Lorry Delivery
* **Request:**
  ```json
  { "jobId": 12, "lorryNumber": "WP-CAR-4402", "balanceToCollect": 80000.00 }
  ```

---

### Member 5: Finished Furniture Inventory Master
#### `GET /inventory/finished` — List Showroom Stock
* **Response (200 OK):**
  ```json
  [
    {
      "productId": 1,
      "skuCode": "FURN-BED-001",
      "itemName": "King Size Teak Bed",
      "category": "Bed",
      "retailPrice": 125000.00,
      "currentStock": 3
    }
  ]
  ```

---

### Member 6: Raw Materials & Workshop Supplies
#### `GET /inventory/raw-materials/reorder-alerts` — List Low Stock Warnings
* **Response (200 OK):**
  ```json
  [
    {
      "materialId": 1,
      "materialCode": "RAW-TIM-01",
      "materialName": "Teak Timber Planks (2x4x8)",
      "currentStock": 14.50,
      "reorderLevel": 25.00,
      "unit": "BOARD_FEET",
      "status": "CRITICAL_LOW"
    }
  ]
  ```

---

### Member 7: Employee Profiles & Base Salary Payroll
#### `GET /employees` — List Staff Directory
* **Response (200 OK):**
  ```json
  [
    {
      "employeeId": 1,
      "fullName": "Kamal Gunasekara",
      "employmentType": "HYBRID",
      "baseMonthlySalary": 45000.00,
      "status": "ACTIVE"
    }
  ]
  ```

---

### Member 8: Artisan Piecework & Job Labor Allocations
#### `POST /piecework/credits/approve` — Approve Completed Job Credit
* **Request:**
  ```json
  { "creditId": 5, "approvedByUserId": 1 }
  ```
* **Response (200 OK):**
  ```json
  { "creditId": 5, "employeeId": 1, "creditedAmount": 8000.00, "isApproved": true }
  ```

---

### Member 9: Worker Cash Advances & Daily Petty Cash
#### `POST /advances` — Issue Cash Advance
* **Request:**
  ```json
  { "employeeId": 1, "amount": 5000.00, "reason": "Family medical advance" }
  ```
* **Response (201 Created):**
  ```json
  { "advanceId": 18, "employeeId": 1, "amount": 5000.00, "isSettled": false }
  ```

---

### Member 10: Executive Analytics & WhatsApp Dispatch
#### `GET /analytics/dashboard-kpis` — Owner Summary Cards
* **Response (200 OK):**
  ```json
  {
    "todayCashCollected": 87500.00,
    "outstandingCustomerDebtors": 248000.00,
    "activeWorkshopJobs": 9,
    "lowStockMaterialsCount": 3
  }
  ```
#### `GET /analytics/whatsapp-link/{orderId}` — Generate WhatsApp Receipt Link
* **Response (200 OK):**
  ```json
  {
    "whatsappUrl": "https://wa.me/94774581290?text=Dear%20Sunil,%20your%20advance%20deposit%20of%20Rs.40,000%20is%20received%20for%20WoodFlow%20Order%20ORD-4512.%20Balance:%20Rs.80,000"
  }
  ```
