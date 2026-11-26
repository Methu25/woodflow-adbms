-- ============================================================================
-- Migration 010: Member 1 (Yohan Methusael E J - 36999)
-- Module: Showroom POS & Counter Billing
-- Extends showroom_invoices & showroom_invoice_items, adds cash_counter_transactions,
-- pos_till_sessions, and vw_daily_counter_sales without breaking existing triggers
-- (trg_pos_stock_deduct) or existing showroom data.
-- ============================================================================

BEGIN;

ALTER TABLE public.showroom_invoices
    ADD COLUMN IF NOT EXISTS subtotal_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (subtotal_amount >= 0),
    ADD COLUMN IF NOT EXISTS discount_code VARCHAR(40),
    ADD COLUMN IF NOT EXISTS discount_percent DECIMAL(5,2) NOT NULL DEFAULT 0.00 CHECK (discount_percent >= 0 AND discount_percent <= 100),
    ADD COLUMN IF NOT EXISTS discount_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (discount_amount >= 0),
    ADD COLUMN IF NOT EXISTS cash_received DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (cash_received >= 0),
    ADD COLUMN IF NOT EXISTS change_due DECIMAL(12,2) NOT NULL DEFAULT 0.00 CHECK (change_due >= 0),
    ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(60),
    ADD COLUMN IF NOT EXISTS invoice_status VARCHAR(20) NOT NULL DEFAULT 'PAID'
        CHECK (invoice_status IN ('PAID', 'PENDING', 'VOIDED', 'REFUNDED')),
    ADD COLUMN IF NOT EXISTS is_custom_order_deposit BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
    ADD COLUMN IF NOT EXISTS billed_by_name VARCHAR(100) NOT NULL DEFAULT 'Yohan Methusael E J',
    ADD COLUMN IF NOT EXISTS void_reason TEXT,
    ADD COLUMN IF NOT EXISTS voided_by INT REFERENCES public.users(user_id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS voided_at TIMESTAMP WITH TIME ZONE;

UPDATE public.showroom_invoices
SET subtotal_amount = total_amount
WHERE subtotal_amount = 0 AND total_amount > 0;

ALTER TABLE public.showroom_invoice_items
    ADD COLUMN IF NOT EXISTS item_name_snapshot VARCHAR(120),
    ADD COLUMN IF NOT EXISTS sku_code_snapshot VARCHAR(30),
    ADD COLUMN IF NOT EXISTS is_refunded BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS public.cash_counter_transactions (
    transaction_id BIGSERIAL PRIMARY KEY,
    invoice_id INT REFERENCES public.showroom_invoices(invoice_id) ON DELETE SET NULL,
    till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
    transaction_type VARCHAR(30) NOT NULL
        CHECK (transaction_type IN ('OPENING_FLOAT', 'CASH_SALE', 'CARD_SALE', 'BANK_TRANSFER_SALE', 'VOID_REFUND', 'SAFE_DEPOSIT')),
    payment_mode VARCHAR(20) NOT NULL
        CHECK (payment_mode IN ('CASH', 'CARD', 'BANK_TRANSFER')),
    amount DECIMAL(12,2) NOT NULL,
    cash_received DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    change_due DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    reference_code VARCHAR(60),
    recorded_by INT REFERENCES public.users(user_id) ON DELETE SET NULL,
    recorded_by_name VARCHAR(100) NOT NULL DEFAULT 'Yohan Methusael E J',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_cash_counter_tx_created_at
    ON public.cash_counter_transactions(created_at DESC);

CREATE TABLE IF NOT EXISTS public.pos_till_sessions (
    session_id SERIAL PRIMARY KEY,
    session_date DATE NOT NULL UNIQUE,
    till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
    opening_float DECIMAL(12,2) NOT NULL DEFAULT 25000.00 CHECK (opening_float >= 0),
    opened_at VARCHAR(20) NOT NULL DEFAULT '08:30 AM',
    notes_5000_count INT NOT NULL DEFAULT 26 CHECK (notes_5000_count >= 0),
    notes_1000_count INT NOT NULL DEFAULT 38 CHECK (notes_1000_count >= 0),
    notes_500_count INT NOT NULL DEFAULT 8 CHECK (notes_500_count >= 0),
    notes_100_count INT NOT NULL DEFAULT 12 CHECK (notes_100_count >= 0),
    coins_50_count INT NOT NULL DEFAULT 6 CHECK (coins_50_count >= 0),
    counted_cash_amount DECIMAL(12,2) NOT NULL DEFAULT 173500.00 CHECK (counted_cash_amount >= 0),
    expected_cash_amount DECIMAL(12,2) NOT NULL DEFAULT 173500.00 CHECK (expected_cash_amount >= 0),
    variance_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    gross_day_revenue DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    safe_deposit_cash DECIMAL(12,2) NOT NULL DEFAULT 0.00,
    session_status VARCHAR(20) NOT NULL DEFAULT 'OPEN'
        CHECK (session_status IN ('OPEN', 'CLOSED')),
    z_report_number VARCHAR(40),
    closed_by_name VARCHAR(100),
    verified_by_name VARCHAR(100),
    closed_at TIMESTAMP WITH TIME ZONE
);

CREATE OR REPLACE VIEW public.vw_daily_counter_sales AS
SELECT
    DATE(si.invoice_date AT TIME ZONE 'Asia/Colombo') AS sale_date,
    COUNT(*) FILTER (WHERE si.invoice_status IN ('PAID', 'PENDING')) AS active_invoice_count,
    COUNT(*) FILTER (WHERE si.payment_mode = 'CASH' AND si.invoice_status = 'PAID') AS cash_invoice_count,
    COUNT(*) FILTER (WHERE si.payment_mode = 'CARD' AND si.invoice_status = 'PAID') AS card_slip_count,
    COUNT(*) FILTER (WHERE si.payment_mode = 'BANK_TRANSFER' AND si.invoice_status IN ('PAID', 'PENDING')) AS bank_slip_count,
    COALESCE(SUM(si.total_amount) FILTER (WHERE si.payment_mode = 'CASH' AND si.invoice_status = 'PAID'), 0.00) AS cash_sales_total,
    COALESCE(SUM(si.total_amount) FILTER (WHERE si.payment_mode = 'CARD' AND si.invoice_status = 'PAID'), 0.00) AS card_sales_total,
    COALESCE(SUM(si.total_amount) FILTER (WHERE si.payment_mode = 'BANK_TRANSFER' AND si.invoice_status IN ('PAID', 'PENDING')), 0.00) AS bank_transfer_total,
    COALESCE(SUM(si.total_amount) FILTER (WHERE si.invoice_status IN ('PAID', 'PENDING')), 0.00) AS gross_revenue,
    COALESCE(SUM(si.discount_amount) FILTER (WHERE si.invoice_status IN ('PAID', 'PENDING')), 0.00) AS total_discounts_granted,
    COUNT(*) FILTER (WHERE si.invoice_status IN ('VOIDED', 'REFUNDED')) AS voided_or_refunded_count
FROM public.showroom_invoices si
GROUP BY DATE(si.invoice_date AT TIME ZONE 'Asia/Colombo');

-- Seed showroom POS walk-in & repeat customers matching Screens 01 & 02
INSERT INTO public.customers (full_name, phone_number, whatsapp_number, delivery_address) VALUES
('Nimal Silva', '+94 77 123 4567', '+94 77 123 4567', 'Walk-in · Kottawa Showroom'),
('Kavinda Bandara', '077 842 1190', '077 842 1190', 'No. 18, High Level Road, Pannipitiya'),
('Sunethra J.', '071 553 9082', '071 553 9082', '42/B, Mattegoda Housing Scheme, Kottawa'),
('Dilshan M.', '076 910 3341', '076 910 3341', 'Walk-in · Kottawa Showroom'),
('Ceylon Timber Co.', '011 284 7720', '077 284 7720', 'Industrial Zone, Homagama'),
('Hasini Peiris', '077 612 4409', '077 612 4409', 'No. 9, Rukmalgama Road, Kottawa'),
('Roshan Silva', '075 339 8120', '075 339 8120', 'Walk-in · Kottawa Showroom'),
('Amaya Fernando', '077 998 2301', '077 998 2301', '74/1, Old Road, Maharagama')
ON CONFLICT (phone_number) DO NOTHING;

COMMIT;
