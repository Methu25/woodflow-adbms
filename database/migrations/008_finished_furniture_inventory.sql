-- ============================================================================
-- Migration 008: Member 5 (P. Rejishanth - 36791)
-- Module: Finished Furniture Inventory Master & Showroom Stock Control
-- Extends finished_furniture & showroom_stock_movements and adds
-- finished_furniture_categories without breaking existing data or triggers.
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS public.finished_furniture_categories (
    category_id SERIAL PRIMARY KEY,
    category_code VARCHAR(20) UNIQUE NOT NULL,
    category_name VARCHAR(50) UNIQUE NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE public.finished_furniture
    ADD COLUMN IF NOT EXISTS barcode VARCHAR(30),
    ADD COLUMN IF NOT EXISTS primary_wood_type VARCHAR(80) NOT NULL DEFAULT 'Seasoned Ceylon Teak',
    ADD COLUMN IF NOT EXISTS min_reorder_level INT NOT NULL DEFAULT 3 CHECK (min_reorder_level >= 0),
    ADD COLUMN IF NOT EXISTS dimensions_finish VARCHAR(180) NOT NULL DEFAULT 'Standard Showroom Finish',
    ADD COLUMN IF NOT EXISTS description_notes TEXT NOT NULL DEFAULT 'Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days.',
    ADD COLUMN IF NOT EXISTS showroom_zone VARCHAR(60) NOT NULL DEFAULT 'Ground Floor · Bay A2',
    ADD COLUMN IF NOT EXISTS image_key VARCHAR(40) NOT NULL DEFAULT 'lounge',
    ADD COLUMN IF NOT EXISTS restock_requested BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS restock_requested_qty INT NOT NULL DEFAULT 0 CHECK (restock_requested_qty >= 0),
    ADD COLUMN IF NOT EXISTS workshop_status_label VARCHAR(60);

ALTER TABLE public.showroom_stock_movements
    ADD COLUMN IF NOT EXISTS notes TEXT;

-- Expand allowed movement_type values safely if needed
ALTER TABLE public.showroom_stock_movements
    DROP CONSTRAINT IF EXISTS showroom_stock_movements_movement_type_check;
ALTER TABLE public.showroom_stock_movements
    ADD CONSTRAINT showroom_stock_movements_movement_type_check
    CHECK (movement_type IN ('SHOWROOM_INTAKE', 'POS_SALE', 'WORKSHOP_COMPLETED', 'STOCK_CORRECTION', 'CUSTOM_DELIVERY', 'SHOWROOM_RETURN'));

-- Seed standard categories matching Screen 01 & Screen 05
INSERT INTO public.finished_furniture_categories (category_code, category_name, description, is_active, display_order) VALUES
('CAT-01', 'Living Room', 'Lounge suites, 3-seater sofas, coffee tables & TV consoles', TRUE, 1),
('CAT-02', 'Bedroom', 'King/queen teak beds, sliding wardrobes & dressing tables', TRUE, 2),
('CAT-03', 'Dining Room', 'Solid teak 4/6/8-chair dining sets, buffets & sideboards', TRUE, 3),
('CAT-04', 'Office', 'Executive desks, conference tables & bookcase cabinets', TRUE, 4),
('CAT-05', 'Kids & Nursery', 'Compact study desks & bunk beds (Seasonal production)', FALSE, 5),
('CAT-06', 'Verandah & Outdoor', 'Weather-resistant treated Burma teak garden & patio furniture', TRUE, 6)
ON CONFLICT (category_name) DO NOTHING;

-- Seed/Upsert Screen 01 – Screen 07 showroom furniture items
INSERT INTO public.finished_furniture (
    sku_code, barcode, item_name, category, primary_wood_type, retail_price,
    current_stock, min_reorder_level, dimensions_finish, description_notes,
    showroom_zone, image_key, is_active, restock_requested, restock_requested_qty, workshop_status_label
) VALUES
(
    'FF-LIV-001', '8901245001928', 'Aurelia 3-Piece Lounge Set', 'Living Room', 'Solid Teak',
    145000.00, 8, 4, '84" × 32" × 30" (3-Seater) · Matte PU Clear Lacquer + 32D Foam Cushions',
    'Hand-crafted solid Burma & Ceylon teak lounge suite with removable linen upholstery. Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days.',
    'Ground Floor · Bay A2', 'lounge', TRUE, FALSE, 0, NULL
),
(
    'FF-BED-004', '8901245001935', 'Kandy Royal King Teak Bed', 'Bedroom', '72"×78"',
    168500.00, 5, 3, '72" × 78" King Frame · Hand-Carved Headboard · Satin Teak Finish',
    'Royal Kandy heritage king bed crafted from kiln-dried Grade-A Ceylon teak with reinforced mortise-and-tenon joints.',
    'First Floor · Bay B1', 'bed', TRUE, FALSE, 0, NULL
),
(
    'FF-DIN-002', '8901245001942', 'Riviera 6-Chair Dining Set', 'Dining Room', 'Burma Teak',
    192000.00, 2, 4, '72" × 42" Table + 6 Upholstered Teak Dining Chairs · High-Gloss PU',
    'Six-seater solid Burma teak dining suite with stain-resistant fabric seat cushions and bevelled table edge.',
    'Ground Floor · Bay C1', 'dining', TRUE, FALSE, 3, NULL
),
(
    'FF-OFF-009', '8901245001959', 'Moratuwa Executive Suite', 'Office', 'Polished Mahogany',
    118000.00, 4, 3, '66" × 34" Pedestal Desk + Credenza · Rich Mahogany Lacquer',
    'Executive office desk with brass hardware, lockable file drawers, and integrated cable routing.',
    'First Floor · Bay D2', 'office', TRUE, FALSE, 0, NULL
),
(
    'FF-LIV-007', '8901245001966', 'Ceylon Heritage Sofa Bed', 'Living Room', 'Cushioned',
    94500.00, 0, 3, '76" × 36" Pull-Out Teak Daybed · 32D High-Density Cushioning',
    'Dual-purpose convertible Ceylon teak sofa bed designed for compact urban living rooms.',
    'Ground Floor · Bay A4', 'sofabed', TRUE, TRUE, 4, 'Urgent Restock'
),
(
    'FF-BED-012', '8901245001973', 'Royal Arch 3-Door Wardrobe', 'Bedroom', 'Nadun & Teak',
    210000.00, 1, 3, '78" × 60" × 24" 3-Door Almirah · Full Bevelled Dressing Mirror',
    'Spacious three-door solid teak and Nadun wardrobe featuring brass locks, hanging rails, and internal jewellery safe drawer.',
    'First Floor · Bay B3', 'wardrobe', TRUE, FALSE, 3, NULL
),
(
    'FF-LIV-011', '8901245001980', 'Kottawa Carved Nadun Coffee Table', 'Living Room', 'Solid Nadun',
    48000.00, 0, 4, '48" × 26" × 18" · Tempered Glass Inset + Lower Magazine Shelf',
    'Traditional carved Nadun center coffee table with bevelled glass top.',
    'Ground Floor · Bay A3', 'coffee', TRUE, TRUE, 4, 'Urgent Restock'
),
(
    'FF-BED-008', '8901245001997', 'Mahogany Queen Storage Bed', 'Bedroom', 'Red Mahogany',
    134000.00, 2, 3, '60" × 78" Queen Hydraulic Storage Frame · Red Mahogany Finish',
    'Queen size mahogany bed with under-bed storage compartments and slatted teak support.',
    'First Floor · Bay B2', 'queenbed', TRUE, TRUE, 2, 'Job #148 Active'
),
(
    'FF-DIN-009', '8901245002000', 'Colonial Teak Glass Display Almirah', 'Dining Room', 'Ceylon Teak',
    156000.00, 1, 2, '74" × 44" × 18" · Crown Moulding & Tempered Glass Shelves',
    'Dutch-colonial inspired glazed crockery cabinet crafted from seasoned Ceylon teak.',
    'Ground Floor · Bay C3', 'almirah', TRUE, FALSE, 2, NULL
),
(
    'FF-LIV-014', '8901245002017', 'Teak Verandah Lounge Chair', 'Living Room', 'Burma Teak',
    64000.00, 6, 3, '30" × 34" × 36" · Woven Rattan Backrest & Teak Armrests',
    'Classic plantation verandah easy chair with angled teak armrests and breathable cane weave.',
    'Ground Floor · Bay A1', 'chair', TRUE, FALSE, 0, NULL
)
ON CONFLICT (sku_code) DO UPDATE SET
    barcode = EXCLUDED.barcode,
    item_name = EXCLUDED.item_name,
    category = EXCLUDED.category,
    primary_wood_type = EXCLUDED.primary_wood_type,
    retail_price = EXCLUDED.retail_price,
    current_stock = EXCLUDED.current_stock,
    min_reorder_level = EXCLUDED.min_reorder_level,
    dimensions_finish = EXCLUDED.dimensions_finish,
    description_notes = EXCLUDED.description_notes,
    showroom_zone = EXCLUDED.showroom_zone,
    image_key = EXCLUDED.image_key,
    restock_requested = EXCLUDED.restock_requested,
    restock_requested_qty = EXCLUDED.restock_requested_qty,
    workshop_status_label = EXCLUDED.workshop_status_label;

COMMIT;
