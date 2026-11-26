-- ============================================================================
-- Migration 009: Member 6 (Muhammed A.T. - 37155)
-- Module: Raw Materials & Workshop Supplies Inventory Master & Supplier Directory
-- Idempotent schema for raw materials, suppliers, receipts (GRN), and workshop consumption
-- ============================================================================

BEGIN;

-- 1. Ensure uuid-ossp or pgcrypto extension for gen_random_uuid
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 2. Master Table: raw_materials
CREATE TABLE IF NOT EXISTS public.raw_materials (
    material_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    material_code VARCHAR(30) UNIQUE NOT NULL,
    name VARCHAR(120) NOT NULL,
    category VARCHAR(50) NOT NULL CHECK (category IN ('Timber', 'Boards', 'Hardware', 'Finishing', 'Fabric', 'Adhesives')),
    unit_of_measure VARCHAR(20) NOT NULL CHECK (unit_of_measure IN ('cu.ft', 'sheets', 'pcs', 'liters', 'meters', 'kg')),
    quantity_on_hand NUMERIC(12,2) NOT NULL DEFAULT 0.00 CHECK (quantity_on_hand >= 0),
    reorder_level NUMERIC(12,2) NOT NULL DEFAULT 10.00 CHECK (reorder_level >= 0),
    unit_cost NUMERIC(12,2) NOT NULL DEFAULT 0.00 CHECK (unit_cost >= 0),
    storage_location VARCHAR(80) NOT NULL DEFAULT 'Main Yard · Bay A',
    notes TEXT DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Idempotently add any columns if raw_materials already existed with older schema
ALTER TABLE public.raw_materials
    ADD COLUMN IF NOT EXISTS name VARCHAR(120),
    ADD COLUMN IF NOT EXISTS category VARCHAR(50) DEFAULT 'Timber',
    ADD COLUMN IF NOT EXISTS quantity_on_hand NUMERIC(12,2) DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS storage_location VARCHAR(80) DEFAULT 'Main Yard · Bay A',
    ADD COLUMN IF NOT EXISTS notes TEXT DEFAULT '',
    ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

-- 3. Master Table: suppliers
CREATE TABLE IF NOT EXISTS public.suppliers (
    supplier_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    supplier_code VARCHAR(20) UNIQUE NOT NULL,
    company_name VARCHAR(120) NOT NULL,
    contact_person VARCHAR(100) NOT NULL DEFAULT '',
    phone VARCHAR(30) NOT NULL DEFAULT '',
    email VARCHAR(100) NOT NULL DEFAULT '',
    address VARCHAR(200) NOT NULL DEFAULT '',
    primary_category VARCHAR(50) NOT NULL DEFAULT 'Timber',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    rating NUMERIC(3,2) DEFAULT 4.50,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 4. Mapping Table: material_suppliers
CREATE TABLE IF NOT EXISTS public.material_suppliers (
    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
    supplier_id UUID NOT NULL REFERENCES public.suppliers(supplier_id) ON DELETE CASCADE,
    unit_price NUMERIC(12,2) NOT NULL DEFAULT 0.00 CHECK (unit_price >= 0),
    lead_time_days INT NOT NULL DEFAULT 3 CHECK (lead_time_days >= 0),
    is_preferred BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (material_id, supplier_id)
);

-- 5. Goods Receipt Note (GRN) Ledger: raw_material_receipts
CREATE TABLE IF NOT EXISTS public.raw_material_receipts (
    receipt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    grn_number VARCHAR(30) UNIQUE NOT NULL,
    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
    supplier_id UUID REFERENCES public.suppliers(supplier_id) ON DELETE SET NULL,
    quantity_received NUMERIC(12,2) NOT NULL CHECK (quantity_received > 0),
    unit_cost NUMERIC(12,2) NOT NULL CHECK (unit_cost >= 0),
    moisture_pct NUMERIC(5,2) DEFAULT 12.00,
    received_by VARCHAR(100) NOT NULL DEFAULT 'Workshop Foreman',
    notes TEXT DEFAULT '',
    received_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 6. Workshop Consumption Ledger: job_materials
CREATE TABLE IF NOT EXISTS public.job_materials (
    job_material_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id VARCHAR(50) NOT NULL DEFAULT 'JOB-WORKSHOP-GEN',
    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
    quantity_issued NUMERIC(12,2) NOT NULL CHECK (quantity_issued > 0),
    artisan_name VARCHAR(100) NOT NULL DEFAULT 'Senior Artisan',
    notes TEXT DEFAULT '',
    issued_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 7. Audit log table check (if not existing)
CREATE TABLE IF NOT EXISTS public.audit_logs (
    log_id SERIAL PRIMARY KEY,
    event_type VARCHAR(60) NOT NULL,
    entity_name VARCHAR(60) NOT NULL,
    entity_id VARCHAR(60),
    details JSONB,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 8. Trigger Function: fn_check_low_raw_stock
CREATE OR REPLACE FUNCTION public.fn_check_low_raw_stock()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.quantity_on_hand <= NEW.reorder_level THEN
        INSERT INTO public.audit_logs (event_type, entity_name, entity_id, details, created_at)
        VALUES (
            'LOW_STOCK_ALERT',
            'raw_materials',
            NEW.material_id::text,
            jsonb_build_object(
                'material_code', NEW.material_code,
                'name', NEW.name,
                'quantity_on_hand', NEW.quantity_on_hand,
                'reorder_level', NEW.reorder_level,
                'unit_of_measure', NEW.unit_of_measure,
                'timestamp', CURRENT_TIMESTAMP
            ),
            CURRENT_TIMESTAMP
        );
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_check_low_raw_stock ON public.raw_materials;
CREATE TRIGGER trg_check_low_raw_stock
    AFTER INSERT OR UPDATE OF quantity_on_hand, reorder_level
    ON public.raw_materials
    FOR EACH ROW
    EXECUTE FUNCTION public.fn_check_low_raw_stock();

-- 9. Analytical Views
CREATE OR REPLACE VIEW public.vw_supplier_directory AS
SELECT
    s.supplier_id,
    s.supplier_code,
    s.company_name,
    s.contact_person,
    s.phone,
    s.email,
    s.address,
    s.primary_category,
    s.is_active,
    s.rating,
    COUNT(ms.material_id) AS materials_supplied_count,
    COALESCE(AVG(ms.lead_time_days), 0)::numeric(5,1) AS avg_lead_time_days
FROM public.suppliers s
LEFT JOIN public.material_suppliers ms ON s.supplier_id = ms.supplier_id
GROUP BY s.supplier_id;

CREATE OR REPLACE VIEW public.vw_raw_material_reorder_queue AS
SELECT
    m.material_id,
    m.material_code,
    m.name,
    m.category,
    m.unit_of_measure,
    m.quantity_on_hand,
    m.reorder_level,
    (m.reorder_level - m.quantity_on_hand) AS deficit_quantity,
    ROUND((m.reorder_level * 1.50) - m.quantity_on_hand, 2) AS suggested_order_qty,
    m.unit_cost,
    s.company_name AS preferred_supplier_name,
    s.phone AS preferred_supplier_phone,
    ms.lead_time_days
FROM public.raw_materials m
LEFT JOIN public.material_suppliers ms ON m.material_id = ms.material_id AND ms.is_preferred IS TRUE
LEFT JOIN public.suppliers s ON ms.supplier_id = s.supplier_id
WHERE m.quantity_on_hand <= m.reorder_level AND m.is_active IS TRUE
ORDER BY (m.quantity_on_hand / NULLIF(m.reorder_level, 0)) ASC;

-- 10. Seed Realistic Sri Lankan Timber, Hardware & Regional Suppliers
INSERT INTO public.suppliers (supplier_id, supplier_code, company_name, contact_person, phone, email, address, primary_category, is_active, rating) VALUES
('a0000001-0000-0000-0000-000000000001', 'SUP-MOR-01', 'Moratuwa Timber Mills Ltd', 'Nimal Jayawardena', '+94 77 123 4567', 'sales@moratuwatimber.lk', 'No. 45 Old Galle Road, Moratuwa', 'Timber', TRUE, 4.90),
('a0000001-0000-0000-0000-000000000002', 'SUP-COL-02', 'Colombo Brass & Hardware Imports', 'Farhan Rameez', '+94 71 889 2341', 'farhan@colombobrass.lk', 'No. 112 Armour Street, Colombo 12', 'Hardware', TRUE, 4.75),
('a0000001-0000-0000-0000-000000000003', 'SUP-LAN-03', 'Lanka Wood Coatings & Chemicals', 'Sunil Mendis', '+94 76 543 9876', 'orders@lankacoatings.lk', 'Industrial Zone, Ekala, Ja-Ela', 'Finishing', TRUE, 4.80),
('a0000001-0000-0000-0000-000000000004', 'SUP-KOT-04', 'Kottawa Ply & Board Center', 'Dhammika Bandara', '+94 70 332 1144', 'dhammika@kottawaply.lk', 'No. 88 High Level Road, Kottawa', 'Boards', TRUE, 4.60),
('a0000001-0000-0000-0000-000000000005', 'SUP-RAT-05', 'Ratnapura Hardwood Suppliers', 'Upali Silva', '+94 72 901 2345', 'upali@ratnapurahardwoods.lk', 'No. 12 Kuruwita Road, Ratnapura', 'Timber', TRUE, 4.85)
ON CONFLICT (supplier_code) DO NOTHING;

INSERT INTO public.raw_materials (material_id, material_code, name, category, unit_of_measure, quantity_on_hand, reorder_level, unit_cost, storage_location, notes, is_active) VALUES
('b0000001-0000-0000-0000-000000000001', 'RAW-TIM-01', 'Burma Teak Plank (2"×4"×8'')', 'Timber', 'cu.ft', 14.50, 25.00, 14500.00, 'Main Shed · Bay 1', 'Grade A Kiln-Dried Teak for premium sofas and dining tables', TRUE),
('b0000001-0000-0000-0000-000000000002', 'RAW-TIM-02', 'Kiln-Dried Mahogany Block (3"×6"×10'')', 'Timber', 'cu.ft', 18.00, 20.00, 9200.00, 'Main Shed · Bay 2', 'Treated mahogany for bed frames and wardrobes', TRUE),
('b0000001-0000-0000-0000-000000000003', 'RAW-TIM-03', 'Ceylon Jackwood Beam (4"×4"×12'')', 'Timber', 'cu.ft', 32.00, 15.00, 11500.00, 'Yard Rack C', 'Naturally pest-resistant structural timber', TRUE),
('b0000001-0000-0000-0000-000000000004', 'RAW-BRD-01', 'Marine Plywood 18mm (8''×4'' Sheet)', 'Boards', 'sheets', 28.00, 15.00, 8400.00, 'Panel Staging Area', 'BWP Grade waterproof plywood for internal cabinetry', TRUE),
('b0000001-0000-0000-0000-000000000005', 'RAW-HRD-01', 'Soft-Close Brass Hinge 4" (Pair)', 'Hardware', 'pcs', 12.00, 30.00, 1250.00, 'Hardware Bin H-04', 'Heavy-duty brass hinges for wardrobe doors', TRUE),
('b0000001-0000-0000-0000-000000000006', 'RAW-FIN-01', 'Jchem Matte Polyurethane Wood Sealer', 'Finishing', 'liters', 4.00, 10.00, 6800.00, 'Chemical Cabinet C-1', 'Two-pack polyurethane spray coating', TRUE),
('b0000001-0000-0000-0000-000000000007', 'RAW-FAB-01', 'Belgian Natural Linen Upholstery Fabric', 'Fabric', 'meters', 45.00, 20.00, 3200.00, 'Upholstery Loft', 'High GSM textured linen fabric in Oatmeal', TRUE),
('b0000001-0000-0000-0000-000000000008', 'RAW-ADH-01', 'D3 Industrial Waterproof Carpenter Glue', 'Adhesives', 'kg', 8.50, 15.00, 2100.00, 'Chemical Cabinet C-2', 'Cross-linking PVA wood glue for joinery', TRUE)
ON CONFLICT (material_code) DO NOTHING;

-- Map Preferred Suppliers
INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred) VALUES
('b0000001-0000-0000-0000-000000000001', 'a0000001-0000-0000-0000-000000000001', 14500.00, 3, TRUE),
('b0000001-0000-0000-0000-000000000002', 'a0000001-0000-0000-0000-000000000001', 9200.00, 3, TRUE),
('b0000001-0000-0000-0000-000000000003', 'a0000001-0000-0000-0000-000000000005', 11500.00, 4, TRUE),
('b0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000004', 8400.00, 2, TRUE),
('b0000001-0000-0000-0000-000000000005', 'a0000001-0000-0000-0000-000000000002', 1250.00, 2, TRUE),
('b0000001-0000-0000-0000-000000000006', 'a0000001-0000-0000-0000-000000000003', 6800.00, 3, TRUE),
('b0000001-0000-0000-0000-000000000008', 'a0000001-0000-0000-0000-000000000003', 2100.00, 2, TRUE)
ON CONFLICT (material_id, supplier_id) DO NOTHING;

-- Initial Seed Goods Receipts
INSERT INTO public.raw_material_receipts (receipt_id, grn_number, material_id, supplier_id, quantity_received, unit_cost, moisture_pct, received_by, notes, received_at) VALUES
('c0000001-0000-0000-0000-000000000001', 'GRN-2026-0101', 'b0000001-0000-0000-0000-000000000001', 'a0000001-0000-0000-0000-000000000001', 20.00, 14500.00, 11.50, 'Sunil Perera (Foreman)', 'Delivered via Moratuwa Lorry WP-GA-4512', TIMESTAMPTZ '2026-11-10 09:30:00+05:30'),
('c0000001-0000-0000-0000-000000000002', 'GRN-2026-0102', 'b0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000004', 30.00, 8400.00, 9.80, 'Sunil Perera (Foreman)', 'Standard delivery of 18mm marine plywood', TIMESTAMPTZ '2026-11-12 14:15:00+05:30')
ON CONFLICT (grn_number) DO NOTHING;

-- Initial Seed Workshop Consumption
INSERT INTO public.job_materials (job_material_id, job_id, material_id, quantity_issued, artisan_name, notes, issued_at) VALUES
('d0000001-0000-0000-0000-000000000001', 'JOB-FRAME-104', 'b0000001-0000-0000-0000-000000000001', 5.50, 'Master Carpenter Piyadasa', 'Framing for Aurelia 3-Seater Sofa', TIMESTAMPTZ '2026-11-13 11:00:00+05:30'),
('d0000001-0000-0000-0000-000000000002', 'JOB-POL-108', 'b0000001-0000-0000-0000-000000000006', 2.00, 'Polisher Gamini', 'First coat for Kandy King Bed headboard', TIMESTAMPTZ '2026-11-14 15:45:00+05:30')
ON CONFLICT DO NOTHING;

COMMIT;
