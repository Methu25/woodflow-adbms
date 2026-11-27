-- ============================================================================
-- Migration 009: Member 6 (Muhammed A.T. / Dhilshan - 37155)
-- Module: Raw Materials & Workshop Supplies Inventory Master & Supplier Directory
-- Extends existing public.raw_materials (INT PK) from schema.sql safely and adds
-- suppliers, material_suppliers, raw_material_receipts, raw_material_consumptions,
-- and raw_material_stock_alerts without modifying protected/shared audit_logs.
-- ============================================================================

BEGIN;

-- 1. Extend existing public.raw_materials from schema.sql safely (preserving INT material_id, material_name, current_stock)
ALTER TABLE public.raw_materials
    ALTER COLUMN material_name TYPE VARCHAR(120);

ALTER TABLE public.raw_materials
    ADD COLUMN IF NOT EXISTS category VARCHAR(50) NOT NULL DEFAULT 'Timber',
    ADD COLUMN IF NOT EXISTS storage_location VARCHAR(80) NOT NULL DEFAULT 'Main Yard · Bay A',
    ADD COLUMN IF NOT EXISTS notes TEXT DEFAULT '',
    ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- 2. Master Table: suppliers
CREATE TABLE IF NOT EXISTS public.suppliers (
    supplier_id SERIAL PRIMARY KEY,
    supplier_code VARCHAR(20) UNIQUE NOT NULL,
    company_name VARCHAR(120) NOT NULL,
    contact_person VARCHAR(100) NOT NULL DEFAULT '',
    phone VARCHAR(30) NOT NULL DEFAULT '',
    email VARCHAR(100) NOT NULL DEFAULT '',
    address VARCHAR(200) NOT NULL DEFAULT '',
    primary_category VARCHAR(50) NOT NULL DEFAULT 'Timber',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    rating NUMERIC(3,2) NOT NULL DEFAULT 4.50,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 3. Mapping Table: material_suppliers
CREATE TABLE IF NOT EXISTS public.material_suppliers (
    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
    supplier_id INT NOT NULL REFERENCES public.suppliers(supplier_id) ON DELETE CASCADE,
    unit_price NUMERIC(12,2) NOT NULL DEFAULT 0.00 CHECK (unit_price >= 0),
    lead_time_days INT NOT NULL DEFAULT 3 CHECK (lead_time_days >= 0),
    is_preferred BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (material_id, supplier_id)
);

-- 4. Goods Receipt Note (GRN) Ledger: raw_material_receipts
CREATE TABLE IF NOT EXISTS public.raw_material_receipts (
    receipt_id SERIAL PRIMARY KEY,
    grn_number VARCHAR(30) UNIQUE NOT NULL,
    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
    supplier_id INT REFERENCES public.suppliers(supplier_id) ON DELETE SET NULL,
    quantity_received NUMERIC(12,2) NOT NULL CHECK (quantity_received > 0),
    unit_cost NUMERIC(12,2) NOT NULL CHECK (unit_cost >= 0),
    moisture_pct NUMERIC(5,2) NOT NULL DEFAULT 12.00,
    received_by VARCHAR(100) NOT NULL DEFAULT 'Workshop Foreman',
    notes TEXT DEFAULT '',
    received_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 5. Decoupled Workshop Consumption Ledger: raw_material_consumptions
-- (Preserves schema.sql's public.job_materials table intact for Production Jobs)
CREATE TABLE IF NOT EXISTS public.raw_material_consumptions (
    consumption_id SERIAL PRIMARY KEY,
    job_reference VARCHAR(50) NOT NULL DEFAULT 'JOB-WORKSHOP-GEN',
    production_job_id INT REFERENCES public.production_jobs(job_id) ON DELETE SET NULL,
    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
    quantity_issued NUMERIC(12,2) NOT NULL CHECK (quantity_issued > 0),
    unit_cost_at_time NUMERIC(12,2) NOT NULL DEFAULT 0.00 CHECK (unit_cost_at_time >= 0),
    artisan_name VARCHAR(100) NOT NULL DEFAULT 'Senior Artisan',
    notes TEXT DEFAULT '',
    issued_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 6. Dedicated Low-Stock Alert Log (owned by Member 6; does not touch Member 10's audit_logs)
CREATE TABLE IF NOT EXISTS public.raw_material_stock_alerts (
    alert_id BIGSERIAL PRIMARY KEY,
    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
    alert_type VARCHAR(30) NOT NULL CHECK (alert_type IN ('LOW_STOCK_ALERT', 'CRITICAL_LOW_STOCK')),
    material_code VARCHAR(30) NOT NULL,
    material_name VARCHAR(120) NOT NULL,
    current_stock NUMERIC(12,2) NOT NULL,
    reorder_level NUMERIC(12,2) NOT NULL,
    unit_of_measure VARCHAR(20) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 7. Trigger Function: fn_check_low_raw_stock
CREATE OR REPLACE FUNCTION public.fn_check_low_raw_stock()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.current_stock <= NEW.reorder_level THEN
        INSERT INTO public.raw_material_stock_alerts (
            material_id, alert_type, material_code, material_name,
            current_stock, reorder_level, unit_of_measure, created_at
        ) VALUES (
            NEW.material_id,
            CASE WHEN NEW.current_stock <= (NEW.reorder_level * 0.50) THEN 'CRITICAL_LOW_STOCK' ELSE 'LOW_STOCK_ALERT' END,
            NEW.material_code,
            NEW.material_name,
            NEW.current_stock,
            NEW.reorder_level,
            NEW.unit_of_measure,
            CURRENT_TIMESTAMP
        );
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_check_low_raw_stock ON public.raw_materials;
CREATE TRIGGER trg_check_low_raw_stock
    AFTER INSERT OR UPDATE OF current_stock, reorder_level
    ON public.raw_materials
    FOR EACH ROW
    EXECUTE FUNCTION public.fn_check_low_raw_stock();

-- 8. Analytical Views
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
    COUNT(ms.material_id)::int AS materials_supplied_count,
    COALESCE(AVG(ms.lead_time_days), 3.0)::numeric(5,1) AS avg_lead_time_days
FROM public.suppliers s
LEFT JOIN public.material_suppliers ms ON s.supplier_id = ms.supplier_id
GROUP BY s.supplier_id;

CREATE OR REPLACE VIEW public.vw_raw_material_reorder_queue AS
SELECT
    m.material_id,
    m.material_code,
    m.material_name,
    m.category,
    m.unit_of_measure,
    m.current_stock,
    m.reorder_level,
    GREATEST(0, m.reorder_level - m.current_stock) AS deficit_quantity,
    ROUND(GREATEST(0, (m.reorder_level * 1.50) - m.current_stock), 2) AS suggested_order_qty,
    m.unit_cost,
    s.company_name AS preferred_supplier_name,
    s.phone AS preferred_supplier_phone,
    ms.lead_time_days
FROM public.raw_materials m
LEFT JOIN public.material_suppliers ms ON m.material_id = ms.material_id AND ms.is_preferred IS TRUE
LEFT JOIN public.suppliers s ON ms.supplier_id = s.supplier_id
WHERE m.current_stock <= m.reorder_level AND m.is_active IS TRUE
ORDER BY (m.current_stock / NULLIF(m.reorder_level, 0)) ASC;

-- 9. Enrich baseline schema.sql seed rows & seed suppliers / additional SKUs
UPDATE public.raw_materials
SET category = CASE
        WHEN material_code LIKE 'RAW-TIM-%' THEN 'Timber'
        WHEN material_code LIKE 'RAW-POL-%' OR material_code LIKE 'RAW-FIN-%' THEN 'Finishing'
        WHEN material_code LIKE 'RAW-FAB-%' THEN 'Fabric'
        ELSE category
    END,
    storage_location = CASE
        WHEN material_code = 'RAW-TIM-01' THEN 'Main Shed · Bay 1'
        WHEN material_code = 'RAW-TIM-02' THEN 'Main Shed · Bay 2'
        WHEN material_code = 'RAW-POL-01' THEN 'Chemical Cabinet C-1'
        WHEN material_code = 'RAW-FAB-01' THEN 'Upholstery Loft'
        ELSE storage_location
    END
WHERE material_code IN ('RAW-TIM-01', 'RAW-TIM-02', 'RAW-POL-01', 'RAW-FAB-01');

INSERT INTO public.suppliers (supplier_code, company_name, contact_person, phone, email, address, primary_category, is_active, rating) VALUES
('SUP-MOR-01', 'Moratuwa Timber Mills Ltd', 'Nimal Jayawardena', '+94 77 123 4567', 'sales@moratuwatimber.lk', 'No. 45 Old Galle Road, Moratuwa', 'Timber', TRUE, 4.90),
('SUP-COL-02', 'Colombo Brass & Hardware Imports', 'Farhan Rameez', '+94 71 889 2341', 'farhan@colombobrass.lk', 'No. 112 Armour Street, Colombo 12', 'Hardware', TRUE, 4.75),
('SUP-LAN-03', 'Lanka Wood Coatings & Chemicals', 'Sunil Mendis', '+94 76 543 9876', 'orders@lankacoatings.lk', 'Industrial Zone, Ekala, Ja-Ela', 'Finishing', TRUE, 4.80),
('SUP-KOT-04', 'Kottawa Ply & Board Center', 'Dhammika Bandara', '+94 70 332 1144', 'dhammika@kottawaply.lk', 'No. 88 High Level Road, Kottawa', 'Boards', TRUE, 4.60),
('SUP-RAT-05', 'Ratnapura Hardwood Suppliers', 'Upali Silva', '+94 72 901 2345', 'upali@ratnapurahardwoods.lk', 'No. 12 Kuruwita Road, Ratnapura', 'Timber', TRUE, 4.85)
ON CONFLICT (supplier_code) DO NOTHING;

INSERT INTO public.raw_materials (material_code, material_name, category, unit_of_measure, current_stock, reorder_level, unit_cost, storage_location, notes, is_active) VALUES
('RAW-TIM-01', 'Burma Teak Plank (2"×4"×8'')', 'Timber', 'cu.ft', 14.50, 25.00, 14500.00, 'Main Shed · Bay 1', 'Grade A Kiln-Dried Teak for premium sofas and dining tables', TRUE),
('RAW-TIM-02', 'Kiln-Dried Mahogany Block (3"×6"×10'')', 'Timber', 'cu.ft', 18.00, 20.00, 9200.00, 'Main Shed · Bay 2', 'Treated mahogany for bed frames and wardrobes', TRUE),
('RAW-TIM-03', 'Ceylon Jackwood Beam (4"×4"×12'')', 'Timber', 'cu.ft', 32.00, 15.00, 11500.00, 'Yard Rack C', 'Naturally pest-resistant structural timber', TRUE),
('RAW-BRD-01', 'Marine Plywood 18mm (8''×4'' Sheet)', 'Boards', 'sheets', 28.00, 15.00, 8400.00, 'Panel Staging Area', 'BWP Grade waterproof plywood for internal cabinetry', TRUE),
('RAW-HRD-01', 'Soft-Close Brass Hinge 4" (Pair)', 'Hardware', 'pcs', 12.00, 30.00, 1250.00, 'Hardware Bin H-04', 'Heavy-duty brass hinges for wardrobe doors', TRUE),
('RAW-FIN-01', 'Jchem Matte Polyurethane Wood Sealer', 'Finishing', 'liters', 4.00, 10.00, 6800.00, 'Chemical Cabinet C-1', 'Two-pack polyurethane spray coating', TRUE),
('RAW-FAB-01', 'Belgian Natural Linen Upholstery Fabric', 'Fabric', 'meters', 45.00, 20.00, 3200.00, 'Upholstery Loft', 'High GSM textured linen fabric in Oatmeal', TRUE),
('RAW-ADH-01', 'D3 Industrial Waterproof Carpenter Glue', 'Adhesives', 'kg', 8.50, 15.00, 2100.00, 'Chemical Cabinet C-2', 'Cross-linking PVA wood glue for joinery', TRUE)
ON CONFLICT (material_code) DO NOTHING;

INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred)
SELECT m.material_id, s.supplier_id, m.unit_cost, v.lead_days, TRUE
FROM (VALUES
    ('RAW-TIM-01', 'SUP-MOR-01', 3),
    ('RAW-TIM-02', 'SUP-MOR-01', 3),
    ('RAW-TIM-03', 'SUP-RAT-05', 4),
    ('RAW-BRD-01', 'SUP-KOT-04', 2),
    ('RAW-HRD-01', 'SUP-COL-02', 2),
    ('RAW-FIN-01', 'SUP-LAN-03', 3),
    ('RAW-ADH-01', 'SUP-LAN-03', 2)
) AS v(mat_code, sup_code, lead_days)
JOIN public.raw_materials m ON m.material_code = v.mat_code
JOIN public.suppliers s ON s.supplier_code = v.sup_code
ON CONFLICT (material_id, supplier_id) DO NOTHING;

INSERT INTO public.raw_material_receipts (grn_number, material_id, supplier_id, quantity_received, unit_cost, moisture_pct, received_by, notes, received_at)
SELECT v.grn_no, m.material_id, s.supplier_id, v.qty, m.unit_cost, v.moisture, v.rec_by, v.notes, v.rec_at
FROM (VALUES
    ('GRN-2026-0101', 'RAW-TIM-01', 'SUP-MOR-01', 20.00, 11.50, 'Sunil Perera (Foreman)', 'Delivered via Moratuwa Lorry WP-GA-4512', TIMESTAMPTZ '2026-11-10 09:30:00+05:30'),
    ('GRN-2026-0102', 'RAW-BRD-01', 'SUP-KOT-04', 30.00, 9.80, 'Sunil Perera (Foreman)', 'Standard delivery of 18mm marine plywood', TIMESTAMPTZ '2026-11-12 14:15:00+05:30')
) AS v(grn_no, mat_code, sup_code, qty, moisture, rec_by, notes, rec_at)
JOIN public.raw_materials m ON m.material_code = v.mat_code
JOIN public.suppliers s ON s.supplier_code = v.sup_code
ON CONFLICT (grn_number) DO NOTHING;

INSERT INTO public.raw_material_consumptions (job_reference, material_id, quantity_issued, unit_cost_at_time, artisan_name, notes, issued_at)
SELECT v.job_ref, m.material_id, v.qty, m.unit_cost, v.artisan, v.notes, v.issued_at
FROM (VALUES
    ('JOB-FRAME-104', 'RAW-TIM-01', 5.50, 'Master Carpenter Piyadasa', 'Framing for Aurelia 3-Seater Sofa', TIMESTAMPTZ '2026-11-13 11:00:00+05:30'),
    ('JOB-POL-108', 'RAW-FIN-01', 2.00, 'Polisher Gamini', 'First coat for Kandy King Bed headboard', TIMESTAMPTZ '2026-11-14 15:45:00+05:30')
) AS v(job_ref, mat_code, qty, artisan, notes, issued_at)
JOIN public.raw_materials m ON m.material_code = v.mat_code
WHERE NOT EXISTS (
    SELECT 1 FROM public.raw_material_consumptions c
    WHERE c.job_reference = v.job_ref AND c.material_id = m.material_id
);

COMMIT;
