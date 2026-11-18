-- ============================================================================
-- WoodFlow Database Schema (PostgreSQL 16)
-- Target: Wimalasiri Furniture, High Level Road, Kottawa
-- Academic Module: CS304.3 Advanced Database Management Systems
-- Team: 10 Full-Stack Modular Vertical Slices
-- Consolidated structure through migration 007. Run once in an EMPTY database.
-- Existing installations must use outstanding migrations, not reapply this file.
-- ============================================================================

-- Enable UUID extension if required
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================================
-- 1. CORE ADMINISTRATION & USERS (SHARED FOUNDATION)
-- ============================================================================

CREATE TABLE IF NOT EXISTS users (
    user_id SERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name VARCHAR(100) NOT NULL,
    role VARCHAR(30) NOT NULL CHECK (role IN ('OWNER', 'ASSISTANT')),
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    account_status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE'
        CONSTRAINT users_account_status_check CHECK (account_status IN ('ACTIVE', 'SUSPENDED', 'DEACTIVATED')),
    auth_version INTEGER NOT NULL DEFAULT 0
);

CREATE OR REPLACE FUNCTION public.sync_user_account_status() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        IF NEW.is_active IS NOT TRUE AND NEW.account_status = 'ACTIVE' THEN
            NEW.account_status := 'DEACTIVATED';
        END IF;
    ELSE
        IF OLD.account_status = 'DEACTIVATED' AND
            (NEW.account_status <> 'DEACTIVATED' OR NEW.is_active IS TRUE) THEN
            RAISE EXCEPTION 'Deactivated accounts cannot be restored' USING ERRCODE = '23514';
        END IF;
        IF NEW.account_status = OLD.account_status AND NEW.is_active IS DISTINCT FROM OLD.is_active THEN
            NEW.account_status := CASE WHEN NEW.is_active IS TRUE THEN 'ACTIVE' ELSE 'DEACTIVATED' END;
        END IF;
        IF NEW.account_status IS DISTINCT FROM OLD.account_status OR NEW.role IS DISTINCT FROM OLD.role THEN
            NEW.auth_version := OLD.auth_version + 1;
        END IF;
    END IF;
    NEW.is_active := (NEW.account_status = 'ACTIVE');
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER users_sync_account_status BEFORE INSERT OR UPDATE ON public.users
FOR EACH ROW EXECUTE FUNCTION public.sync_user_account_status();

-- Role-wide assistant permissions; no administrative permission keys are editable.
CREATE TABLE IF NOT EXISTS public.assistant_permission_settings (
    id SMALLINT PRIMARY KEY CHECK (id = 1),
    granted_keys TEXT[] NOT NULL DEFAULT ARRAY['dashboard.view']::TEXT[],
    version BIGINT NOT NULL DEFAULT 0 CHECK (version >= 0)
);
INSERT INTO public.assistant_permission_settings (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.assistant_user_permissions (
    user_id INTEGER PRIMARY KEY REFERENCES public.users(user_id) ON DELETE RESTRICT,
    granted_keys TEXT[], -- NULL means inherit the ASSISTANT role defaults.
    version BIGINT NOT NULL DEFAULT 0 CHECK (version >= 0)
);
-- Retain the version on reset, preventing stale edits after override/reset cycles.
-- Role changes discard old overrides so they cannot resurface after promotion/demotion.
CREATE OR REPLACE FUNCTION public.clear_assistant_permission_override() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.role IS DISTINCT FROM OLD.role THEN
        UPDATE public.assistant_user_permissions SET granted_keys = NULL, version = version + 1
        WHERE user_id = NEW.user_id;
    END IF;
    RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER users_clear_permission_override AFTER UPDATE OF role ON public.users
FOR EACH ROW EXECUTE FUNCTION public.clear_assistant_permission_override();

-- Secure first-owner bootstrap state. Existing installations apply migration 002 manually.
CREATE TABLE IF NOT EXISTS owner_setup_state (
    id SMALLINT PRIMARY KEY CHECK (id = 1),
    code_hash BYTEA CHECK (code_hash IS NULL OR octet_length(code_hash) = 32),
    expires_at TIMESTAMPTZ,
    consumed_at TIMESTAMPTZ,
    initialized_at TIMESTAMPTZ,
    initialized_owner_id INT REFERENCES users(user_id) ON DELETE RESTRICT,
    CHECK ((code_hash IS NULL) = (expires_at IS NULL)),
    CHECK (initialized_at IS NULL OR (code_hash IS NULL AND consumed_at IS NOT NULL))
);
INSERT INTO owner_setup_state (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

-- Password recovery: final structure from migrations 006 and 007.
-- New installations start with no requests or reset-code hashes.
CREATE TABLE IF NOT EXISTS public.password_recovery_requests (
    request_id UUID PRIMARY KEY,
    requester_user_id INTEGER NOT NULL REFERENCES public.users(user_id),
    selected_owner_id INTEGER NOT NULL REFERENCES public.users(user_id),
    status VARCHAR(12) NOT NULL DEFAULT 'PENDING'
        CHECK (status IN ('PENDING','APPROVED','COMPLETED','DENIED','EXPIRED')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    expires_at TIMESTAMPTZ NOT NULL,
    requester_auth_version INTEGER NOT NULL,
    processed_by_owner_id INTEGER REFERENCES public.users(user_id),
    approving_owner_auth_version INTEGER,
    processed_at TIMESTAMPTZ,
    code_hash BYTEA,
    consumed_at TIMESTAMPTZ,
    previous_owner_id INTEGER REFERENCES public.users(user_id),
    resolved_by_owner_id INTEGER REFERENCES public.users(user_id),
    resolved_at TIMESTAMPTZ,
    CHECK (requester_user_id <> selected_owner_id),
    CHECK (processed_by_owner_id IS NULL OR processed_by_owner_id = selected_owner_id),
    CHECK (code_hash IS NULL OR octet_length(code_hash) = 32),
    CHECK ((status = 'APPROVED') = (code_hash IS NOT NULL)),
    CHECK ((status = 'COMPLETED') = (consumed_at IS NOT NULL)),
    CHECK (status NOT IN ('APPROVED','COMPLETED','DENIED') OR
        (processed_by_owner_id IS NOT NULL AND processed_at IS NOT NULL)),
    CHECK (status NOT IN ('APPROVED','COMPLETED') OR approving_owner_auth_version IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS recovery_one_open_per_user
    ON public.password_recovery_requests(requester_user_id) WHERE status IN ('PENDING','APPROVED');
CREATE UNIQUE INDEX IF NOT EXISTS recovery_code_hash
    ON public.password_recovery_requests(code_hash) WHERE code_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS recovery_owner_inbox
    ON public.password_recovery_requests(selected_owner_id, created_at DESC);
CREATE INDEX IF NOT EXISTS recovery_requester_history
    ON public.password_recovery_requests(requester_user_id, created_at DESC);

-- Server-side authentication sessions: only hashes of random handles are stored.
-- No sessions are seeded. Logout revocation and account versioning remain independent.
CREATE TABLE IF NOT EXISTS public.auth_sessions (
    session_hash BYTEA PRIMARY KEY CHECK (octet_length(session_hash)=32),
    user_id INTEGER NOT NULL REFERENCES public.users(user_id) ON DELETE RESTRICT,
    auth_version INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    expires_at TIMESTAMPTZ NOT NULL DEFAULT (clock_timestamp()+interval '8 hours'),
    revoked_at TIMESTAMPTZ,
    CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS auth_sessions_expiry ON public.auth_sessions(expires_at);
CREATE INDEX IF NOT EXISTS auth_sessions_user ON public.auth_sessions(user_id);

CREATE TABLE IF NOT EXISTS audit_logs (
    log_id BIGSERIAL PRIMARY KEY,
    user_id INT REFERENCES users(user_id) ON DELETE SET NULL,
    action VARCHAR(50) NOT NULL,
    entity_name VARCHAR(50) NOT NULL,
    entity_id INT NOT NULL,
    old_values JSONB,
    new_values JSONB,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- 2. CUSTOMERS (SHARED ACROSS SALES & WORKSHOP)
-- ============================================================================

CREATE TABLE IF NOT EXISTS customers (
    customer_id SERIAL PRIMARY KEY,
    full_name VARCHAR(100) NOT NULL,
    phone_number VARCHAR(20) UNIQUE NOT NULL,
    whatsapp_number VARCHAR(20),
    delivery_address TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- 3. RETAIL INVENTORY: FINISHED FURNITURE (MEMBER 5)
-- ============================================================================

CREATE TABLE IF NOT EXISTS finished_furniture (
    product_id SERIAL PRIMARY KEY,
    sku_code VARCHAR(30) UNIQUE NOT NULL,
    item_name VARCHAR(100) NOT NULL,
    category VARCHAR(50) NOT NULL, -- e.g., 'Bed', 'Dining Set', 'Wardrobe', 'Sofa'
    retail_price DECIMAL(12,2) NOT NULL CHECK (retail_price >= 0),
    current_stock INT NOT NULL DEFAULT 0 CHECK (current_stock >= 0),
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS showroom_stock_movements (
    movement_id BIGSERIAL PRIMARY KEY,
    product_id INT NOT NULL REFERENCES finished_furniture(product_id) ON DELETE RESTRICT,
    movement_type VARCHAR(30) NOT NULL CHECK (movement_type IN ('SHOWROOM_INTAKE', 'POS_SALE', 'WORKSHOP_COMPLETED', 'STOCK_CORRECTION')),
    quantity_changed INT NOT NULL,
    stock_before INT NOT NULL,
    stock_after INT NOT NULL,
    reference_id VARCHAR(50),
    performed_by INT REFERENCES users(user_id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- 4. SHOWROOM POS & COUNTER BILLING (MEMBER 1)
-- ============================================================================

CREATE TABLE IF NOT EXISTS showroom_invoices (
    invoice_id SERIAL PRIMARY KEY,
    invoice_number VARCHAR(30) UNIQUE NOT NULL,
    customer_id INT REFERENCES customers(customer_id) ON DELETE SET NULL,
    total_amount DECIMAL(12,2) NOT NULL CHECK (total_amount >= 0),
    payment_mode VARCHAR(20) NOT NULL CHECK (payment_mode IN ('CASH', 'CARD', 'BANK_TRANSFER')),
    invoice_date TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    billed_by INT REFERENCES users(user_id)
);

CREATE TABLE IF NOT EXISTS showroom_invoice_items (
    item_id SERIAL PRIMARY KEY,
    invoice_id INT NOT NULL REFERENCES showroom_invoices(invoice_id) ON DELETE CASCADE,
    product_id INT NOT NULL REFERENCES finished_furniture(product_id) ON DELETE RESTRICT,
    quantity INT NOT NULL CHECK (quantity > 0),
    unit_price DECIMAL(12,2) NOT NULL CHECK (unit_price >= 0),
    subtotal DECIMAL(12,2) GENERATED ALWAYS AS (quantity * unit_price) STORED
);

-- ============================================================================
-- 5. CUSTOM QUOTATIONS & 1/3 DEPOSIT ENGINE (MEMBER 2)
-- ============================================================================

CREATE TABLE IF NOT EXISTS custom_orders (
    order_id SERIAL PRIMARY KEY,
    order_number VARCHAR(30) UNIQUE NOT NULL,
    customer_id INT NOT NULL REFERENCES customers(customer_id) ON DELETE RESTRICT,
    furniture_description TEXT NOT NULL,
    agreed_price DECIMAL(12,2) NOT NULL CHECK (agreed_price > 0),
    min_deposit_required DECIMAL(12,2) GENERATED ALWAYS AS (ROUND(agreed_price / 3.0, 2)) STORED,
    deposit_paid DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (deposit_paid >= 0),
    remaining_balance DECIMAL(12,2) GENERATED ALWAYS AS (agreed_price - deposit_paid) STORED,
    order_status VARCHAR(30) DEFAULT 'DEPOSIT_PENDING' 
        CHECK (order_status IN ('DEPOSIT_PENDING', 'CONFIRMED', 'IN_PRODUCTION', 'READY_FOR_DELIVERY', 'COMPLETED', 'CANCELLED')),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS order_deposits (
    deposit_id SERIAL PRIMARY KEY,
    order_id INT NOT NULL REFERENCES custom_orders(order_id) ON DELETE RESTRICT,
    deposit_amount DECIMAL(12,2) NOT NULL CHECK (deposit_amount > 0),
    payment_method VARCHAR(20) NOT NULL CHECK (payment_method IN ('CASH', 'BANK_TRANSFER', 'CARD')),
    reference_number VARCHAR(50),
    payment_date TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    received_by INT REFERENCES users(user_id)
);

-- ============================================================================
-- 6. WORKSHOP PRODUCTION: CARPENTRY & FINISHING (MEMBERS 3 & 4)
-- ============================================================================

CREATE TABLE IF NOT EXISTS production_jobs (
    job_id SERIAL PRIMARY KEY,
    job_ticket_no VARCHAR(30) UNIQUE NOT NULL,
    order_id INT REFERENCES custom_orders(order_id) ON DELETE SET NULL, -- Nullable for decoupling
    furniture_type VARCHAR(100) NOT NULL,
    timber_type VARCHAR(50) NOT NULL, -- 'Teak', 'Mahogany', 'Nadun', etc.
    custom_dimensions TEXT,
    varnish_finish VARCHAR(50),
    current_stage VARCHAR(30) NOT NULL DEFAULT 'DESIGN_APPROVAL'
        CHECK (current_stage IN (
            'DESIGN_APPROVAL',           -- Stage 1
            'TIMBER_CUTTING_FRAMEWORK',  -- Stage 2
            'CUSHIONING_UPHOLSTERY',     -- Stage 3
            'SANDING_SPRAY_POLISHING',   -- Stage 4
            'QUALITY_INSPECTION',        -- Stage 5
            'DELIVERY'                   -- Stage 6
        )),
    target_delivery_date DATE,
    is_completed BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Stage 1 & 2 Framework Tracking (Member 3)
CREATE TABLE IF NOT EXISTS framework_logs (
    framework_id SERIAL PRIMARY KEY,
    job_id INT UNIQUE NOT NULL REFERENCES production_jobs(job_id) ON DELETE CASCADE,
    design_approved BOOLEAN DEFAULT FALSE,
    design_approved_date TIMESTAMP WITH TIME ZONE,
    timber_cut_date TIMESTAMP WITH TIME ZONE,
    framework_completed_date TIMESTAMP WITH TIME ZONE,
    lead_carpenter_id INT -- References employees table
);

-- Stage 3, 4, 5, 6 Finishing & Delivery Tracking (Member 4)
CREATE TABLE IF NOT EXISTS finishing_logs (
    finishing_id SERIAL PRIMARY KEY,
    job_id INT UNIQUE NOT NULL REFERENCES production_jobs(job_id) ON DELETE CASCADE,
    cushioning_done BOOLEAN DEFAULT FALSE,
    cushioning_date TIMESTAMP WITH TIME ZONE,
    spray_polish_done BOOLEAN DEFAULT FALSE,
    spray_polish_date TIMESTAMP WITH TIME ZONE,
    quality_passed BOOLEAN DEFAULT FALSE,
    inspection_notes TEXT,
    delivery_date TIMESTAMP WITH TIME ZONE,
    delivery_lorry_no VARCHAR(20),
    balance_collected_at_door DECIMAL(12,2) DEFAULT 0.00
);

-- ============================================================================
-- 7. RAW MATERIALS & WORKSHOP SUPPLIES (MEMBER 6)
-- ============================================================================

CREATE TABLE IF NOT EXISTS raw_materials (
    material_id SERIAL PRIMARY KEY,
    material_code VARCHAR(30) UNIQUE NOT NULL,
    material_name VARCHAR(100) NOT NULL,
    unit_of_measure VARCHAR(20) NOT NULL, -- 'BOARD_FEET', 'METERS', 'GALLONS', 'BOXES'
    current_stock DECIMAL(10,2) NOT NULL DEFAULT 0.00 CHECK (current_stock >= 0),
    reorder_level DECIMAL(10,2) NOT NULL DEFAULT 10.00,
    unit_cost DECIMAL(10,2) NOT NULL CHECK (unit_cost >= 0),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS job_materials (
    allocation_id SERIAL PRIMARY KEY,
    job_id INT NOT NULL REFERENCES production_jobs(job_id) ON DELETE CASCADE,
    material_id INT NOT NULL REFERENCES raw_materials(material_id) ON DELETE RESTRICT,
    quantity_allocated DECIMAL(10,2) NOT NULL CHECK (quantity_allocated > 0),
    unit_cost_at_time DECIMAL(10,2) NOT NULL,
    allocated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- 8. WORKFORCE & ARTISAN LABOR (MEMBERS 7, 8 & 9)
-- ============================================================================

-- Staff Directory (Member 7)
CREATE TABLE IF NOT EXISTS employees (
    employee_id SERIAL PRIMARY KEY,
    full_name VARCHAR(100) NOT NULL,
    nic_number VARCHAR(15) UNIQUE NOT NULL,
    phone_number VARCHAR(20) NOT NULL,
    employment_type VARCHAR(30) NOT NULL CHECK (employment_type IN ('MONTHLY_SALARIED', 'PIECEWORK_ONLY', 'HYBRID')),
    base_monthly_salary DECIMAL(10,2) DEFAULT 0.00,
    is_active BOOLEAN DEFAULT TRUE,
    joined_date DATE DEFAULT CURRENT_DATE
);

CREATE TABLE IF NOT EXISTS attendance_records (
    attendance_id SERIAL PRIMARY KEY,
    employee_id INT NOT NULL REFERENCES employees(employee_id) ON DELETE RESTRICT,
    work_date DATE NOT NULL DEFAULT CURRENT_DATE,
    status VARCHAR(20) NOT NULL CHECK (status IN ('PRESENT', 'ABSENT', 'HALF_DAY')),
    UNIQUE(employee_id, work_date)
);

-- Artisan Piecework (Member 8)
CREATE TABLE IF NOT EXISTS piecework_credits (
    credit_id SERIAL PRIMARY KEY,
    job_id INT REFERENCES production_jobs(job_id) ON DELETE SET NULL,
    employee_id INT NOT NULL REFERENCES employees(employee_id) ON DELETE RESTRICT,
    stage_assigned VARCHAR(50) NOT NULL, -- e.g., 'Bed Frame Carpentry', 'Spray Polishing'
    agreed_labor_cost DECIMAL(10,2) NOT NULL CHECK (agreed_labor_cost > 0),
    is_approved BOOLEAN DEFAULT FALSE,
    approved_by INT REFERENCES users(user_id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Worker Cash Advances & Daily Petty Cash (Member 9)
CREATE TABLE IF NOT EXISTS worker_advances (
    advance_id SERIAL PRIMARY KEY,
    employee_id INT NOT NULL REFERENCES employees(employee_id) ON DELETE RESTRICT,
    amount DECIMAL(10,2) NOT NULL CHECK (amount > 0),
    request_date DATE DEFAULT CURRENT_DATE,
    is_settled BOOLEAN DEFAULT FALSE,
    issued_by INT REFERENCES users(user_id)
);

CREATE TABLE IF NOT EXISTS petty_cash_ledger (
    entry_id SERIAL PRIMARY KEY,
    transaction_date DATE NOT NULL DEFAULT CURRENT_DATE,
    entry_type VARCHAR(20) NOT NULL CHECK (entry_type IN ('CASH_IN', 'CASH_OUT')),
    category VARCHAR(50) NOT NULL, -- 'WORKER_ADVANCE', 'TEA_SNACKS', 'TRANSPORT', 'WORKSHOP_HARDWARE', 'DRAWER_REPLENISHMENT'
    amount DECIMAL(10,2) NOT NULL CHECK (amount > 0),
    description TEXT,
    logged_by INT REFERENCES users(user_id),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS monthly_payroll_settlements (
    settlement_id SERIAL PRIMARY KEY,
    employee_id INT NOT NULL REFERENCES employees(employee_id) ON DELETE RESTRICT,
    settlement_period VARCHAR(7) NOT NULL, -- 'YYYY-MM'
    base_salary_paid DECIMAL(10,2) DEFAULT 0.00,
    total_piecework_earned DECIMAL(10,2) DEFAULT 0.00,
    advances_deducted DECIMAL(10,2) DEFAULT 0.00,
    net_payout DECIMAL(10,2) NOT NULL,
    settled_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- ============================================================================
-- 9. ADVANCED DATABASE CONCEPTS: PROCEDURES, TRIGGERS & VIEWS
-- ============================================================================

-- A. ACID Transaction Stored Procedure: 1/3 Deposit Validation & Order Creation (Member 2)
CREATE OR REPLACE PROCEDURE sp_create_order_with_deposit(
    p_customer_id INT,
    p_furniture_desc TEXT,
    p_agreed_price DECIMAL,
    p_deposit_amount DECIMAL,
    p_payment_method VARCHAR,
    p_user_id INT,
    OUT p_order_id INT,
    OUT p_order_number VARCHAR
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_min_deposit DECIMAL(12,2);
BEGIN
    v_min_deposit := ROUND(p_agreed_price / 3.0, 2);

    -- Enforce Mandatory 1/3 Advance Rule
    IF p_deposit_amount < v_min_deposit THEN
        RAISE EXCEPTION 'Deposit amount Rs. % is below the mandatory 1/3 deposit of Rs. %',
            p_deposit_amount, v_min_deposit;
    END IF;

    -- Generate Unique Order Number: ORD-YYYYMMDD-XXXX
    p_order_number := 'ORD-' || TO_CHAR(CURRENT_DATE, 'YYYYMMDD') || '-' || LPAD(FLOOR(RANDOM()*10000)::TEXT, 4, '0');

    -- Insert Custom Order Header
    INSERT INTO custom_orders (
        order_number, customer_id, furniture_description, agreed_price,
        deposit_paid, order_status
    ) VALUES (
        p_order_number, p_customer_id, p_furniture_desc, p_agreed_price,
        p_deposit_amount, 'CONFIRMED'
    ) RETURNING order_id INTO p_order_id;

    -- Record in Deposit Ledger atomically
    INSERT INTO order_deposits (
        order_id, deposit_amount, payment_method, received_by
    ) VALUES (
        p_order_id, p_deposit_amount, p_payment_method, p_user_id
    );

    -- Auto-spawn Production Job Ticket
    INSERT INTO production_jobs (
        job_ticket_no, order_id, furniture_type, timber_type, current_stage
    ) VALUES (
        'JOB-' || TO_CHAR(CURRENT_DATE, 'YYMM') || '-' || LPAD(p_order_id::TEXT, 4, '0'),
        p_order_id, p_furniture_desc, 'Teak', 'DESIGN_APPROVAL'
    );
END;
$$;

-- B. Stored Procedure: Settle Monthly Payroll (Base + Piecework - Advances) (Members 7, 8 & 9)
CREATE OR REPLACE PROCEDURE sp_settle_monthly_payroll(
    p_employee_id INT,
    p_period VARCHAR, -- 'YYYY-MM'
    OUT p_net_payout DECIMAL
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_base_salary DECIMAL(10,2) := 0.00;
    v_piecework DECIMAL(10,2) := 0.00;
    v_advances DECIMAL(10,2) := 0.00;
BEGIN
    -- Get base salary
    SELECT COALESCE(base_monthly_salary, 0.00) INTO v_base_salary
    FROM employees WHERE employee_id = p_employee_id;

    -- Aggregate approved piecework
    SELECT COALESCE(SUM(agreed_labor_cost), 0.00) INTO v_piecework
    FROM piecework_credits
    WHERE employee_id = p_employee_id AND is_approved = TRUE;

    -- Aggregate unsettled advances
    SELECT COALESCE(SUM(amount), 0.00) INTO v_advances
    FROM worker_advances
    WHERE employee_id = p_employee_id AND is_settled = FALSE;

    -- Calculate net payout
    p_net_payout := (v_base_salary + v_piecework) - v_advances;

    -- Record settlement
    INSERT INTO monthly_payroll_settlements (
        employee_id, settlement_period, base_salary_paid, total_piecework_earned, advances_deducted, net_payout
    ) VALUES (
        p_employee_id, p_period, v_base_salary, v_piecework, v_advances, p_net_payout
    );

    -- Mark advances as settled
    UPDATE worker_advances
    SET is_settled = TRUE
    WHERE employee_id = p_employee_id AND is_settled = FALSE;
END;
$$;

-- C. Trigger: Automatically Deduct Finished Furniture Stock on POS Sale (Member 1 & 5)
CREATE OR REPLACE FUNCTION fn_trg_deduct_showroom_stock()
RETURNS TRIGGER AS $$
DECLARE
    v_stock_before INT;
    v_stock_after INT;
BEGIN
    SELECT current_stock INTO v_stock_before FROM finished_furniture WHERE product_id = NEW.product_id;
    
    IF v_stock_before < NEW.quantity THEN
        RAISE EXCEPTION 'Insufficient showroom stock for product ID %. Available: %, Requested: %',
            NEW.product_id, v_stock_before, NEW.quantity;
    END IF;

    v_stock_after := v_stock_before - NEW.quantity;

    -- Update inventory table
    UPDATE finished_furniture
    SET current_stock = v_stock_after
    WHERE product_id = NEW.product_id;

    -- Log to Stock Movement Ledger
    INSERT INTO showroom_stock_movements (
        product_id, movement_type, quantity_changed, stock_before, stock_after, reference_id
    ) VALUES (
        NEW.product_id, 'POS_SALE', -NEW.quantity, v_stock_before, v_stock_after, 'INV-' || NEW.invoice_id::TEXT
    );

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_pos_stock_deduct
AFTER INSERT ON showroom_invoice_items
FOR EACH ROW
EXECUTE FUNCTION fn_trg_deduct_showroom_stock();

-- D. Trigger: Log Workshop Stage Transitions into Audit Logs (Member 3 & 4)
CREATE OR REPLACE FUNCTION fn_trg_log_stage_change()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.current_stage IS DISTINCT FROM NEW.current_stage THEN
        INSERT INTO audit_logs (
            action, entity_name, entity_id, old_values, new_values
        ) VALUES (
            'STAGE_CHANGE', 'production_jobs', NEW.job_id,
            jsonb_build_object('stage', OLD.current_stage),
            jsonb_build_object('stage', NEW.current_stage)
        );
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE TRIGGER trg_log_stage_change
AFTER UPDATE ON production_jobs
FOR EACH ROW
EXECUTE FUNCTION fn_trg_log_stage_change();

-- E. Analytical Views for Executive Dashboard (Member 10)
CREATE OR REPLACE VIEW vw_customer_outstanding_balances AS
SELECT 
    c.customer_id,
    c.full_name,
    c.phone_number,
    c.whatsapp_number,
    COUNT(co.order_id) AS total_custom_orders,
    COALESCE(SUM(co.agreed_price), 0.00) AS total_quotation_value,
    COALESCE(SUM(co.deposit_paid), 0.00) AS total_deposits_received,
    COALESCE(SUM(co.remaining_balance), 0.00) AS total_balance_due
FROM customers c
JOIN custom_orders co ON c.customer_id = co.customer_id
WHERE co.remaining_balance > 0
GROUP BY c.customer_id, c.full_name, c.phone_number, c.whatsapp_number;

CREATE MATERIALIZED VIEW IF NOT EXISTS mat_vw_monthly_profit_summary AS
SELECT 
    TO_CHAR(si.invoice_date, 'YYYY-MM') AS report_month,
    COUNT(DISTINCT si.invoice_id) AS total_pos_sales_count,
    COALESCE(SUM(si.total_amount), 0.00) AS total_revenue
FROM showroom_invoices si
GROUP BY TO_CHAR(si.invoice_date, 'YYYY-MM');

CREATE UNIQUE INDEX IF NOT EXISTS idx_mat_vw_month ON mat_vw_monthly_profit_summary(report_month);

-- ============================================================================
-- 10. REALISTIC SEED DATA (FOR IMMEDIATE TESTING & DEMO)
-- ============================================================================

-- Demo placeholders cannot authenticate. Never treat them as initialized owners.
-- Bootstrap creates a separate account with an unused username; no automatic cleanup.
-- Production installations should omit these demo rows.
-- Initial Users
INSERT INTO users (username, password_hash, full_name, role) VALUES
('wimalasiri', 'hash_owner_123', 'Wimalasiri (Owner)', 'OWNER'),
('kasun_cashier', 'hash_cashier_123', 'Kasun (Counter Clerk)', 'ASSISTANT'),
('bandara_foreman', 'hash_foreman_123', 'Bandara (Workshop Lead)', 'ASSISTANT')
ON CONFLICT (username) DO NOTHING;

-- Initial Customers
INSERT INTO customers (full_name, phone_number, whatsapp_number, delivery_address) VALUES
('Sunil Jayakody', '0774581290', '0774581290', 'No 45, Pannipitiya Road, Kottawa'),
('Dr. Nimal Perera', '0718893421', '0718893421', '12/A, Hospital Road, Maharagama'),
('Kumari Silva', '0761239845', '0761239845', 'Athurugiriya Road, Malabe')
ON CONFLICT (phone_number) DO NOTHING;

-- Initial Finished Goods
INSERT INTO finished_furniture (sku_code, item_name, category, retail_price, current_stock) VALUES
('FURN-BED-001', 'King Size Teak Bed (Modern Slat Design)', 'Bed', 125000.00, 3),
('FURN-DIN-002', '6-Seater Mahogany Dining Table with Chairs', 'Dining Set', 165000.00, 2),
('FURN-WAR-003', '3-Door Teak Wardrobe (Fitted Mirror)', 'Wardrobe', 145000.00, 1),
('FURN-SOF-004', 'L-Shape Velvet Sofa (Ash Grey Fabric)', 'Sofa', 185000.00, 2)
ON CONFLICT (sku_code) DO NOTHING;

-- Initial Raw Materials
INSERT INTO raw_materials (material_code, material_name, unit_of_measure, current_stock, reorder_level, unit_cost) VALUES
('RAW-TIM-01', 'Teak Timber Planks (2"x4"x8'')', 'BOARD_FEET', 45.00, 25.00, 1450.00),
('RAW-TIM-02', 'Mahogany Slabs (Grade A)', 'BOARD_FEET', 60.00, 20.00, 1200.00),
('RAW-POL-01', 'Jchem High Gloss Polyurethane Spray Polish', 'GALLONS', 8.00, 4.00, 8200.00),
('RAW-FAB-01', 'Heavy GSM Ash Grey Velvet Upholstery Fabric', 'METERS', 35.00, 15.00, 2100.00)
ON CONFLICT (material_code) DO NOTHING;

-- Initial Employees
INSERT INTO employees (full_name, nic_number, phone_number, employment_type, base_monthly_salary) VALUES
('Kamal Gunasekara', '198214500213', '0714589912', 'HYBRID', 45000.00),
('Jagath Kumara', '197931400892', '0723341190', 'PIECEWORK_ONLY', 0.00),
('Nihal Silva', '198545900114', '0778890041', 'MONTHLY_SALARIED', 65000.00)
ON CONFLICT (nic_number) DO NOTHING;

-- Initial Petty Cash Entries
INSERT INTO petty_cash_ledger (transaction_date, entry_type, category, amount, description) VALUES
(CURRENT_DATE, 'CASH_IN', 'DRAWER_REPLENISHMENT', 25000.00, 'Opening daily petty cash float from owner'),
(CURRENT_DATE, 'CASH_OUT', 'TEA_SNACKS', 850.00, 'Morning tea and bread for workshop craftsmen'),
(CURRENT_DATE, 'CASH_OUT', 'WORKSHOP_HARDWARE', 1400.00, 'Emergency brass screws and sandpaper sheets')
ON CONFLICT DO NOTHING;
