using System.Data;
using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class FinishedFurnitureRepository(Database database) : IFinishedFurnitureRepository
{
    private readonly SemaphoreSlim _schemaInitLock = new(1, 1);
    private bool _schemaInitialized;

    private async Task EnsureExtendedSchemaAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        if (_schemaInitialized) return;
        await _schemaInitLock.WaitAsync(ct);
        try
        {
            if (_schemaInitialized) return;

            const string sql = """
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
                    ADD COLUMN IF NOT EXISTS min_reorder_level INT NOT NULL DEFAULT 3,
                    ADD COLUMN IF NOT EXISTS dimensions_finish VARCHAR(180) NOT NULL DEFAULT 'Standard Showroom Finish',
                    ADD COLUMN IF NOT EXISTS description_notes TEXT NOT NULL DEFAULT 'Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days.',
                    ADD COLUMN IF NOT EXISTS showroom_zone VARCHAR(60) NOT NULL DEFAULT 'Ground Floor · Bay A2',
                    ADD COLUMN IF NOT EXISTS image_key VARCHAR(40) NOT NULL DEFAULT 'lounge',
                    ADD COLUMN IF NOT EXISTS restock_requested BOOLEAN NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS restock_requested_qty INT NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS workshop_status_label VARCHAR(60);

                ALTER TABLE public.showroom_stock_movements
                    ADD COLUMN IF NOT EXISTS notes TEXT;

                ALTER TABLE public.showroom_stock_movements
                    DROP CONSTRAINT IF EXISTS showroom_stock_movements_movement_type_check;
                ALTER TABLE public.showroom_stock_movements
                    ADD CONSTRAINT showroom_stock_movements_movement_type_check
                    CHECK (movement_type IN ('SHOWROOM_INTAKE', 'POS_SALE', 'WORKSHOP_COMPLETED', 'STOCK_CORRECTION', 'CUSTOM_DELIVERY', 'SHOWROOM_RETURN'));
                """;

            await using (var cmd = new NpgsqlCommand(sql, connection))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SeedReferenceCatalogIfMissingAsync(connection, ct);
            _schemaInitialized = true;
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }

    private static async Task SeedReferenceCatalogIfMissingAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        const string seedCategories = """
            INSERT INTO public.finished_furniture_categories (category_code, category_name, description, is_active, display_order) VALUES
            ('CAT-01', 'Living Room', 'Lounge suites, 3-seater sofas, coffee tables & TV consoles', TRUE, 1),
            ('CAT-02', 'Bedroom', 'King/queen teak beds, sliding wardrobes & dressing tables', TRUE, 2),
            ('CAT-03', 'Dining Room', 'Solid teak 4/6/8-chair dining sets, buffets & sideboards', TRUE, 3),
            ('CAT-04', 'Office', 'Executive desks, conference tables & bookcase cabinets', TRUE, 4),
            ('CAT-05', 'Kids & Nursery', 'Compact study desks & bunk beds (Seasonal production)', FALSE, 5),
            ('CAT-06', 'Verandah & Outdoor', 'Weather-resistant treated Burma teak garden & patio furniture', TRUE, 6)
            ON CONFLICT (category_name) DO NOTHING;
            """;

        await using (var catCmd = new NpgsqlCommand(seedCategories, connection))
        {
            await catCmd.ExecuteNonQueryAsync(ct);
        }

        // Check if FF-LIV-001 exists; if not, seed the Figma showroom catalog items
        await using var checkCmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.finished_furniture WHERE sku_code = 'FF-LIV-001')", connection);
        var hasFigmaSeed = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (hasFigmaSeed) return;

        const string seedItems = """
            INSERT INTO public.finished_furniture (
                sku_code, barcode, item_name, category, primary_wood_type, retail_price,
                current_stock, min_reorder_level, dimensions_finish, description_notes,
                showroom_zone, image_key, is_active, restock_requested, restock_requested_qty, workshop_status_label
            ) VALUES
            ('FF-LIV-001', '8901245001928', 'Aurelia 3-Piece Lounge Set', 'Living Room', 'Solid Teak', 145000.00, 8, 4, '84" × 32" × 30" (3-Seater) · Matte PU Clear Lacquer + 32D Foam Cushions', 'Hand-crafted solid Burma & Ceylon teak lounge suite with removable linen upholstery. Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days.', 'Ground Floor · Bay A2', 'lounge', TRUE, FALSE, 0, NULL),
            ('FF-BED-004', '8901245001935', 'Kandy Royal King Teak Bed', 'Bedroom', '72"×78"', 168500.00, 5, 3, '72" × 78" King Frame · Hand-Carved Headboard · Satin Teak Finish', 'Royal Kandy heritage king bed crafted from kiln-dried Grade-A Ceylon teak with reinforced mortise-and-tenon joints.', 'First Floor · Bay B1', 'bed', TRUE, FALSE, 0, NULL),
            ('FF-DIN-002', '8901245001942', 'Riviera 6-Chair Dining Set', 'Dining Room', 'Burma Teak', 192000.00, 2, 4, '72" × 42" Table + 6 Upholstered Teak Dining Chairs · High-Gloss PU', 'Six-seater solid Burma teak dining suite with stain-resistant fabric seat cushions and bevelled table edge.', 'Ground Floor · Bay C1', 'dining', TRUE, FALSE, 3, NULL),
            ('FF-OFF-009', '8901245001959', 'Moratuwa Executive Suite', 'Office', 'Polished Mahogany', 118000.00, 4, 3, '66" × 34" Pedestal Desk + Credenza · Rich Mahogany Lacquer', 'Executive office desk with brass hardware, lockable file drawers, and integrated cable routing.', 'First Floor · Bay D2', 'office', TRUE, FALSE, 0, NULL),
            ('FF-LIV-007', '8901245001966', 'Ceylon Heritage Sofa Bed', 'Living Room', 'Cushioned', 94500.00, 0, 3, '76" × 36" Pull-Out Teak Daybed · 32D High-Density Cushioning', 'Dual-purpose convertible Ceylon teak sofa bed designed for compact urban living rooms.', 'Ground Floor · Bay A4', 'sofabed', TRUE, TRUE, 4, 'Urgent Restock'),
            ('FF-BED-012', '8901245001973', 'Royal Arch 3-Door Wardrobe', 'Bedroom', 'Nadun & Teak', 210000.00, 1, 3, '78" × 60" × 24" 3-Door Almirah · Full Bevelled Dressing Mirror', 'Spacious three-door solid teak and Nadun wardrobe featuring brass locks, hanging rails, and internal jewellery safe drawer.', 'First Floor · Bay B3', 'wardrobe', TRUE, FALSE, 3, NULL),
            ('FF-LIV-011', '8901245001980', 'Kottawa Carved Nadun Coffee Table', 'Living Room', 'Solid Nadun', 48000.00, 0, 4, '48" × 26" × 18" · Tempered Glass Inset + Lower Magazine Shelf', 'Traditional carved Nadun center coffee table with bevelled glass top.', 'Ground Floor · Bay A3', 'coffee', TRUE, TRUE, 4, 'Urgent Restock'),
            ('FF-BED-008', '8901245001997', 'Mahogany Queen Storage Bed', 'Bedroom', 'Red Mahogany', 134000.00, 2, 3, '60" × 78" Queen Hydraulic Storage Frame · Red Mahogany Finish', 'Queen size mahogany bed with under-bed storage compartments and slatted teak support.', 'First Floor · Bay B2', 'queenbed', TRUE, TRUE, 2, 'Job #148 Active'),
            ('FF-DIN-009', '8901245002000', 'Colonial Teak Glass Display Almirah', 'Dining Room', 'Ceylon Teak', 156000.00, 1, 2, '74" × 44" × 18" · Crown Moulding & Tempered Glass Shelves', 'Dutch-colonial inspired glazed crockery cabinet crafted from seasoned Ceylon teak.', 'Ground Floor · Bay C3', 'almirah', TRUE, FALSE, 2, NULL),
            ('FF-LIV-014', '8901245002017', 'Teak Verandah Lounge Chair', 'Living Room', 'Burma Teak', 64000.00, 6, 3, '30" × 34" × 36" · Woven Rattan Backrest & Teak Armrests', 'Classic plantation verandah easy chair with angled teak armrests and breathable cane weave.', 'Ground Floor · Bay A1', 'chair', TRUE, FALSE, 0, NULL)
            ON CONFLICT (sku_code) DO NOTHING;

            -- Ensure any legacy seed items also have barcodes and normalized fields
            UPDATE public.finished_furniture
            SET barcode = COALESCE(barcode, '890124500' || LPAD(product_id::text, 4, '0')),
                category = CASE
                    WHEN category IN ('Bed', 'Wardrobe') THEN 'Bedroom'
                    WHEN category = 'Dining Set' THEN 'Dining Room'
                    WHEN category = 'Sofa' THEN 'Living Room'
                    ELSE category
                END,
                image_key = CASE
                    WHEN sku_code LIKE '%BED%' THEN 'bed'
                    WHEN sku_code LIKE '%DIN%' THEN 'dining'
                    WHEN sku_code LIKE '%WAR%' THEN 'wardrobe'
                    WHEN sku_code LIKE '%OFF%' THEN 'office'
                    WHEN sku_code = 'FF-LIV-007' THEN 'sofabed'
                    ELSE image_key
                END
            WHERE barcode IS NULL;
            """;

        await using (var itemCmd = new NpgsqlCommand(seedItems, connection))
        {
            await itemCmd.ExecuteNonQueryAsync(ct);
        }

        // Seed realistic stock movements if ledger is empty
        await using var movCountCmd = new NpgsqlCommand("SELECT count(*)::int FROM public.showroom_stock_movements", connection);
        var movCount = (int)(await movCountCmd.ExecuteScalarAsync(ct))!;
        if (movCount == 0)
        {
            const string seedMovements = """
                INSERT INTO public.showroom_stock_movements (product_id, movement_type, quantity_changed, stock_before, stock_after, reference_id, notes, created_at)
                SELECT p.product_id, v.movement_type, v.qty_changed, v.stock_before, v.stock_after, v.reference_id, v.notes, v.created_at
                FROM (VALUES
                    ('FF-LIV-001', 'POS_SALE', -2, 10, 8, 'INV-2026-0892 · Yohan M.', 'POS Sale (trg_pos_stock_deduct)', TIMESTAMPTZ '2026-11-13 10:42:00+05:30'),
                    ('FF-BED-004', 'WORKSHOP_COMPLETED', 3, 2, 5, 'PROD-JOB-145 · Sanith N.', 'Workshop Finishing In', TIMESTAMPTZ '2026-11-13 09:15:00+05:30'),
                    ('FF-DIN-002', 'CUSTOM_DELIVERY', -1, 3, 2, 'DEL-2026-108 · Dilshan S.', 'Custom Delivery Dispatch', TIMESTAMPTZ '2026-11-12 16:30:00+05:30'),
                    ('FF-BED-012', 'STOCK_CORRECTION', -1, 2, 1, 'AUD-NOV-04 · P Rejishanth', 'Transferred display unit to Kandy branch', TIMESTAMPTZ '2026-11-12 11:20:00+05:30'),
                    ('FF-OFF-009', 'WORKSHOP_COMPLETED', 2, 2, 4, 'PROD-JOB-139 · Rakshan P.', 'Workshop Finishing In', TIMESTAMPTZ '2026-11-11 15:05:00+05:30'),
                    ('FF-LIV-007', 'POS_SALE', -2, 2, 0, 'INV-2026-0874 · Yohan M.', 'POS Sale (trg_pos_stock_deduct)', TIMESTAMPTZ '2026-11-10 14:50:00+05:30'),
                    ('FF-LIV-014', 'SHOWROOM_RETURN', 1, 5, 6, 'RET-2026-019 · P Rejishanth', 'Customer exchange returned to showroom floor', TIMESTAMPTZ '2026-11-09 12:10:00+05:30'),
                    ('FF-LIV-001', 'WORKSHOP_COMPLETED', 4, 6, 10, 'PROD-JOB-132 · Sanith N.', 'Workshop Finishing In', TIMESTAMPTZ '2026-11-08 16:15:00+05:30'),
                    ('FF-LIV-001', 'POS_SALE', -1, 7, 6, 'INV-2026-0841 · Yohan M.', 'POS Sale (trg_pos_stock_deduct)', TIMESTAMPTZ '2026-11-03 13:20:00+05:30')
                ) AS v(sku_code, movement_type, qty_changed, stock_before, stock_after, reference_id, notes, created_at)
                JOIN public.finished_furniture p ON p.sku_code = v.sku_code;
                """;
            await using var seedMovCmd = new NpgsqlCommand(seedMovements, connection);
            await seedMovCmd.ExecuteNonQueryAsync(ct);
        }
    }

    private const string ItemColumns = """
        product_id, sku_code, COALESCE(barcode, '890124500' || LPAD(product_id::text, 4, '0')),
        item_name, category, COALESCE(primary_wood_type, 'Seasoned Ceylon Teak'),
        retail_price, current_stock, COALESCE(min_reorder_level, 3),
        COALESCE(dimensions_finish, 'Standard Showroom Finish'),
        COALESCE(description_notes, 'Includes 10-year structural joinery warranty.'),
        COALESCE(showroom_zone, 'Ground Floor · Bay A2'),
        COALESCE(image_key, 'lounge'),
        COALESCE(is_active, TRUE),
        COALESCE(restock_requested, FALSE),
        COALESCE(restock_requested_qty, 0),
        workshop_status_label,
        created_at
        """;

    private static FinishedFurnitureItem ReadItem(NpgsqlDataReader r) => new(
        r.GetInt32(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        r.GetString(5),
        r.GetDecimal(6),
        r.GetInt32(7),
        r.GetInt32(8),
        r.GetString(9),
        r.GetString(10),
        r.GetString(11),
        r.GetString(12),
        r.GetBoolean(13),
        r.GetBoolean(14),
        r.GetInt32(15),
        r.IsDBNull(16) ? null : r.GetString(16),
        r.IsDBNull(17) ? null : r.GetFieldValue<DateTimeOffset>(17)
    );

    public async Task<IReadOnlyList<FinishedFurnitureItem>> ListItemsAsync(string? category, string? search, bool includeInactive, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = $"""
            SELECT {ItemColumns}
            FROM public.finished_furniture
            WHERE (@includeInactive OR is_active IS TRUE)
            ORDER BY
                CASE WHEN sku_code LIKE 'FF-%' THEN 0 ELSE 1 END,
                product_id ASC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("includeInactive", includeInactive);

        var items = new List<FinishedFurnitureItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    public async Task<FinishedFurnitureItem?> FindByIdAsync(int productId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        return await FindByIdInternalAsync(connection, null, productId, ct);
    }

    private static async Task<FinishedFurnitureItem?> FindByIdInternalAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int productId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {ItemColumns} FROM public.finished_furniture WHERE product_id = @id",
            connection, transaction);
        command.Parameters.AddWithValue("id", productId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadItem(reader) : null;
    }

    public async Task<FinishedFurnitureItem?> FindBySkuAsync(string skuCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var command = new NpgsqlCommand(
            $"SELECT {ItemColumns} FROM public.finished_furniture WHERE lower(sku_code) = lower(@sku) OR barcode = @sku LIMIT 1",
            connection);
        command.Parameters.AddWithValue("sku", skuCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadItem(reader) : null;
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> CreateItemAsync(
        int actorId, string actorName, UpsertFinishedFurnitureRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        await using (var dupCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.finished_furniture WHERE lower(sku_code) = lower(@sku))",
            connection, transaction))
        {
            dupCmd.Parameters.AddWithValue("sku", request.SkuCode);
            if ((bool)(await dupCmd.ExecuteScalarAsync(ct))!)
                return new(Error: FinishedFurnitureWriteError.DuplicateSku, Message: $"SKU '{request.SkuCode}' is already registered.");
        }

        const string insertSql = """
            INSERT INTO public.finished_furniture (
                sku_code, barcode, item_name, category, primary_wood_type, retail_price,
                current_stock, min_reorder_level, dimensions_finish, description_notes,
                showroom_zone, image_key, is_active
            ) VALUES (
                @sku, @barcode, @name, @category, @wood, @price,
                @stock, @reorder, @dimensions, @notes,
                @zone, @imageKey, @isActive
            ) RETURNING product_id
            """;

        await using var insertCmd = new NpgsqlCommand(insertSql, connection, transaction);
        insertCmd.Parameters.AddWithValue("sku", request.SkuCode);
        insertCmd.Parameters.AddWithValue("barcode", request.Barcode);
        insertCmd.Parameters.AddWithValue("name", request.ItemName);
        insertCmd.Parameters.AddWithValue("category", request.Category);
        insertCmd.Parameters.AddWithValue("wood", request.PrimaryWoodType);
        insertCmd.Parameters.AddWithValue("price", request.RetailPrice);
        insertCmd.Parameters.AddWithValue("stock", request.CurrentStock);
        insertCmd.Parameters.AddWithValue("reorder", request.MinReorderLevel);
        insertCmd.Parameters.AddWithValue("dimensions", request.DimensionsFinish);
        insertCmd.Parameters.AddWithValue("notes", request.DescriptionNotes);
        insertCmd.Parameters.AddWithValue("zone", request.ShowroomZone ?? "Ground Floor · Bay A2");
        insertCmd.Parameters.AddWithValue("imageKey", request.ImageKey ?? "lounge");
        insertCmd.Parameters.AddWithValue("isActive", request.IsActive);

        var productId = (int)(await insertCmd.ExecuteScalarAsync(ct))!;

        if (request.CurrentStock > 0)
        {
            const string movSql = """
                INSERT INTO public.showroom_stock_movements (
                    product_id, movement_type, quantity_changed, stock_before, stock_after,
                    reference_id, performed_by, notes
                ) VALUES (
                    @productId, 'SHOWROOM_INTAKE', @qty, 0, @qty,
                    @refId, @actorId, 'Initial showroom opening stock intake'
                )
                """;
            await using var movCmd = new NpgsqlCommand(movSql, connection, transaction);
            movCmd.Parameters.AddWithValue("productId", productId);
            movCmd.Parameters.AddWithValue("qty", request.CurrentStock);
            movCmd.Parameters.AddWithValue("refId", $"INIT-{request.SkuCode} · {actorName}");
            movCmd.Parameters.AddWithValue("actorId", actorId);
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        var created = await FindByIdInternalAsync(connection, transaction, productId, ct);
        await transaction.CommitAsync(ct);
        return new(Data: created);
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> UpdateItemAsync(
        int actorId, string actorName, int productId, UpsertFinishedFurnitureRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var existing = await FindByIdInternalAsync(connection, transaction, productId, ct);
        if (existing is null)
            return new(Error: FinishedFurnitureWriteError.NotFound, Message: "Furniture item not found.");

        await using (var dupCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.finished_furniture WHERE lower(sku_code) = lower(@sku) AND product_id <> @id)",
            connection, transaction))
        {
            dupCmd.Parameters.AddWithValue("sku", request.SkuCode);
            dupCmd.Parameters.AddWithValue("id", productId);
            if ((bool)(await dupCmd.ExecuteScalarAsync(ct))!)
                return new(Error: FinishedFurnitureWriteError.DuplicateSku, Message: $"SKU '{request.SkuCode}' is already used by another item.");
        }

        const string updateSql = """
            UPDATE public.finished_furniture SET
                sku_code = @sku,
                barcode = @barcode,
                item_name = @name,
                category = @category,
                primary_wood_type = @wood,
                retail_price = @price,
                current_stock = @stock,
                min_reorder_level = @reorder,
                dimensions_finish = @dimensions,
                description_notes = @notes,
                showroom_zone = @zone,
                image_key = @imageKey,
                is_active = @isActive
            WHERE product_id = @id
            """;

        await using var updateCmd = new NpgsqlCommand(updateSql, connection, transaction);
        updateCmd.Parameters.AddWithValue("sku", request.SkuCode);
        updateCmd.Parameters.AddWithValue("barcode", request.Barcode);
        updateCmd.Parameters.AddWithValue("name", request.ItemName);
        updateCmd.Parameters.AddWithValue("category", request.Category);
        updateCmd.Parameters.AddWithValue("wood", request.PrimaryWoodType);
        updateCmd.Parameters.AddWithValue("price", request.RetailPrice);
        updateCmd.Parameters.AddWithValue("stock", request.CurrentStock);
        updateCmd.Parameters.AddWithValue("reorder", request.MinReorderLevel);
        updateCmd.Parameters.AddWithValue("dimensions", request.DimensionsFinish);
        updateCmd.Parameters.AddWithValue("notes", request.DescriptionNotes);
        updateCmd.Parameters.AddWithValue("zone", request.ShowroomZone ?? existing.ShowroomZone);
        updateCmd.Parameters.AddWithValue("imageKey", request.ImageKey ?? existing.ImageKey);
        updateCmd.Parameters.AddWithValue("isActive", request.IsActive);
        updateCmd.Parameters.AddWithValue("id", productId);
        await updateCmd.ExecuteNonQueryAsync(ct);

        var delta = request.CurrentStock - existing.CurrentStock;
        if (delta != 0)
        {
            const string movSql = """
                INSERT INTO public.showroom_stock_movements (
                    product_id, movement_type, quantity_changed, stock_before, stock_after,
                    reference_id, performed_by, notes
                ) VALUES (
                    @productId, 'STOCK_CORRECTION', @delta, @before, @after,
                    @refId, @actorId, 'Catalog specification stock count update'
                )
                """;
            await using var movCmd = new NpgsqlCommand(movSql, connection, transaction);
            movCmd.Parameters.AddWithValue("productId", productId);
            movCmd.Parameters.AddWithValue("delta", delta);
            movCmd.Parameters.AddWithValue("before", existing.CurrentStock);
            movCmd.Parameters.AddWithValue("after", request.CurrentStock);
            movCmd.Parameters.AddWithValue("refId", $"SPEC-{request.SkuCode} · {actorName}");
            movCmd.Parameters.AddWithValue("actorId", actorId);
            await movCmd.ExecuteNonQueryAsync(ct);
        }

        var updated = await FindByIdInternalAsync(connection, transaction, productId, ct);
        await transaction.CommitAsync(ct);
        return new(Data: updated);
    }

    public async Task<FinishedFurnitureWriteResult<bool>> DeleteItemAsync(int actorId, int productId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        // Soft deactivate to preserve business history as required by PermissionCatalog ("deletion must respect business history")
        await using var cmd = new NpgsqlCommand(
            "UPDATE public.finished_furniture SET is_active = FALSE WHERE product_id = @id",
            connection);
        cmd.Parameters.AddWithValue("id", productId);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0
            ? new(Data: true)
            : new(Error: FinishedFurnitureWriteError.NotFound, Message: "Furniture item not found.");
    }

    public async Task<IReadOnlyList<ShowroomStockMovementRecord>> ListMovementsAsync(
        int? productId, string? filterType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                m.movement_id,
                m.product_id,
                p.sku_code,
                p.item_name,
                m.movement_type,
                m.quantity_changed,
                m.stock_before,
                m.stock_after,
                m.reference_id,
                m.performed_by,
                COALESCE(u.full_name, 'P Rejishanth'),
                m.notes,
                m.created_at
            FROM public.showroom_stock_movements m
            JOIN public.finished_furniture p ON p.product_id = m.product_id
            LEFT JOIN public.users u ON u.user_id = m.performed_by
            WHERE (@productId IS NULL OR m.product_id = @productId)
              AND (@fromDate IS NULL OR m.created_at >= @fromDate)
              AND (@toDate IS NULL OR m.created_at <= @toDate)
            ORDER BY m.created_at DESC, m.movement_id DESC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter("productId", NpgsqlTypes.NpgsqlDbType.Integer)
        {
            Value = productId.HasValue ? productId.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("fromDate", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = from.HasValue ? from.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("toDate", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = to.HasValue ? to.Value : DBNull.Value
        });

        var list = new List<ShowroomStockMovementRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ShowroomStockMovementRecord(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetFieldValue<DateTimeOffset>(12)
            ));
        }

        return list;
    }

    public async Task<FinishedFurnitureWriteResult<ShowroomStockMovementRecord>> RecordMovementAsync(
        int actorId, string actorName, RecordStockMovementRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var item = await FindByIdInternalAsync(connection, transaction, request.ProductId, ct);
        if (item is null)
            return new(Error: FinishedFurnitureWriteError.NotFound, Message: "Furniture item not found.");

        var newStock = item.CurrentStock + request.QuantityChanged;
        if (newStock < 0)
            return new(Error: FinishedFurnitureWriteError.InsufficientStock,
                Message: $"Insufficient showroom stock for {item.SkuCode}. Available: {item.CurrentStock}, Requested deduction: {Math.Abs(request.QuantityChanged)}.");

        await using (var updateCmd = new NpgsqlCommand(
            "UPDATE public.finished_furniture SET current_stock = @after, restock_requested = CASE WHEN @after > min_reorder_level THEN FALSE ELSE restock_requested END WHERE product_id = @id",
            connection, transaction))
        {
            updateCmd.Parameters.AddWithValue("after", newStock);
            updateCmd.Parameters.AddWithValue("id", item.ProductId);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        var reference = string.IsNullOrWhiteSpace(request.ReferenceId)
            ? $"MOV-{DateTimeOffset.UtcNow:yyyyMMdd} · {actorName}"
            : request.ReferenceId.Trim();

        const string insertMovSql = """
            INSERT INTO public.showroom_stock_movements (
                product_id, movement_type, quantity_changed, stock_before, stock_after,
                reference_id, performed_by, notes
            ) VALUES (
                @productId, @type, @qty, @before, @after,
                @refId, @actorId, @notes
            ) RETURNING movement_id, created_at
            """;

        await using var movCmd = new NpgsqlCommand(insertMovSql, connection, transaction);
        movCmd.Parameters.AddWithValue("productId", item.ProductId);
        movCmd.Parameters.AddWithValue("type", request.MovementType);
        movCmd.Parameters.AddWithValue("qty", request.QuantityChanged);
        movCmd.Parameters.AddWithValue("before", item.CurrentStock);
        movCmd.Parameters.AddWithValue("after", newStock);
        movCmd.Parameters.AddWithValue("refId", reference);
        movCmd.Parameters.AddWithValue("actorId", actorId);
        movCmd.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);

        await using var reader = await movCmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var movId = reader.GetInt64(0);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        await reader.CloseAsync();

        await transaction.CommitAsync(ct);

        return new(Data: new ShowroomStockMovementRecord(
            movId, item.ProductId, item.SkuCode, item.ItemName, request.MovementType,
            request.QuantityChanged, item.CurrentStock, newStock, reference, actorId, actorName, request.Notes, createdAt
        ));
    }

    public async Task<FinishedFurnitureWriteResult<StockAuditSessionResultDto>> ApplyStockAuditAsync(
        int actorId, string actorName, ApplyStockAuditRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var sessionCode = string.IsNullOrWhiteSpace(request.SessionCode)
            ? $"AUD-{DateTimeOffset.UtcNow:yyyy-MM}"
            : request.SessionCode.Trim();

        var adjustedCount = 0;
        var netVariance = 0;
        var updatedDtos = new List<FinishedFurnitureDto>();

        foreach (var auditItem in request.Items)
        {
            var existing = await FindByIdInternalAsync(connection, transaction, auditItem.ProductId, ct);
            if (existing is null)
                return new(Error: FinishedFurnitureWriteError.NotFound, Message: $"Product #{auditItem.ProductId} was not found.");

            var variance = auditItem.PhysicalCount - existing.CurrentStock;
            netVariance += variance;

            if (variance != 0)
            {
                adjustedCount++;
                await using (var updateCmd = new NpgsqlCommand(
                    "UPDATE public.finished_furniture SET current_stock = @physical WHERE product_id = @id",
                    connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("physical", auditItem.PhysicalCount);
                    updateCmd.Parameters.AddWithValue("id", existing.ProductId);
                    await updateCmd.ExecuteNonQueryAsync(ct);
                }

                const string movSql = """
                    INSERT INTO public.showroom_stock_movements (
                        product_id, movement_type, quantity_changed, stock_before, stock_after,
                        reference_id, performed_by, notes
                    ) VALUES (
                        @productId, 'STOCK_CORRECTION', @variance, @before, @after,
                        @refId, @actorId, @notes
                    )
                    """;

                await using var movCmd = new NpgsqlCommand(movSql, connection, transaction);
                movCmd.Parameters.AddWithValue("productId", existing.ProductId);
                movCmd.Parameters.AddWithValue("variance", variance);
                movCmd.Parameters.AddWithValue("before", existing.CurrentStock);
                movCmd.Parameters.AddWithValue("after", auditItem.PhysicalCount);
                movCmd.Parameters.AddWithValue("refId", $"{sessionCode} · {actorName}");
                movCmd.Parameters.AddWithValue("actorId", actorId);
                movCmd.Parameters.AddWithValue("notes", auditItem.Reason.Trim());
                await movCmd.ExecuteNonQueryAsync(ct);
            }

            var refreshed = await FindByIdInternalAsync(connection, transaction, existing.ProductId, ct);
            if (refreshed is not null)
                updatedDtos.Add(FinishedFurnitureService.MapItem(refreshed));
        }

        await transaction.CommitAsync(ct);

        return new(Data: new StockAuditSessionResultDto(
            sessionCode,
            adjustedCount,
            netVariance,
            DateTimeOffset.UtcNow,
            updatedDtos
        ));
    }

    public async Task<IReadOnlyList<FinishedFurnitureCategory>> ListCategoriesAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                c.category_id,
                c.category_code,
                c.category_name,
                c.description,
                c.is_active,
                c.display_order,
                COUNT(f.product_id)::int AS sku_count
            FROM public.finished_furniture_categories c
            LEFT JOIN public.finished_furniture f
                ON lower(f.category) = lower(c.category_name) AND f.is_active IS TRUE
            GROUP BY c.category_id, c.category_code, c.category_name, c.description, c.is_active, c.display_order
            ORDER BY c.display_order ASC, c.category_id ASC
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var list = new List<FinishedFurnitureCategory>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new FinishedFurnitureCategory(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetInt32(5),
                reader.GetInt32(6)
            ));
        }
        return list;
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureCategory>> CreateCategoryAsync(
        CreateCategoryRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using (var dupCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.finished_furniture_categories WHERE lower(category_name) = lower(@name))",
            connection))
        {
            dupCmd.Parameters.AddWithValue("name", request.CategoryName);
            if ((bool)(await dupCmd.ExecuteScalarAsync(ct))!)
                return new(Error: FinishedFurnitureWriteError.DuplicateCategory, Message: $"Category '{request.CategoryName}' already exists.");
        }

        await using var countCmd = new NpgsqlCommand("SELECT count(*)::int + 1 FROM public.finished_furniture_categories", connection);
        var nextNum = (int)(await countCmd.ExecuteScalarAsync(ct))!;
        var code = $"CAT-{nextNum:D2}";

        const string insertSql = """
            INSERT INTO public.finished_furniture_categories (category_code, category_name, description, is_active, display_order)
            VALUES (@code, @name, @desc, @active, @order)
            RETURNING category_id
            """;

        await using var cmd = new NpgsqlCommand(insertSql, connection);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("name", request.CategoryName);
        cmd.Parameters.AddWithValue("desc", request.Description);
        cmd.Parameters.AddWithValue("active", request.IsActive);
        cmd.Parameters.AddWithValue("order", nextNum);
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;

        var all = await ListCategoriesAsync(ct);
        return new(Data: all.First(c => c.CategoryId == id));
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureCategory>> UpdateCategoryAsync(
        int categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var findCmd = new NpgsqlCommand(
            "SELECT category_name, display_order FROM public.finished_furniture_categories WHERE category_id = @id",
            connection, transaction);
        findCmd.Parameters.AddWithValue("id", categoryId);
        await using var reader = await findCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new(Error: FinishedFurnitureWriteError.NotFound, Message: "Category not found.");

        var oldName = reader.GetString(0);
        var oldOrder = reader.GetInt32(1);
        await reader.CloseAsync();

        await using (var dupCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.finished_furniture_categories WHERE lower(category_name) = lower(@name) AND category_id <> @id)",
            connection, transaction))
        {
            dupCmd.Parameters.AddWithValue("name", request.CategoryName);
            dupCmd.Parameters.AddWithValue("id", categoryId);
            if ((bool)(await dupCmd.ExecuteScalarAsync(ct))!)
                return new(Error: FinishedFurnitureWriteError.DuplicateCategory, Message: $"Category '{request.CategoryName}' already exists.");
        }

        await using (var updateCmd = new NpgsqlCommand("""
            UPDATE public.finished_furniture_categories
            SET category_name = @name, description = @desc, is_active = @active, display_order = @order
            WHERE category_id = @id
            """, connection, transaction))
        {
            updateCmd.Parameters.AddWithValue("name", request.CategoryName);
            updateCmd.Parameters.AddWithValue("desc", request.Description);
            updateCmd.Parameters.AddWithValue("active", request.IsActive);
            updateCmd.Parameters.AddWithValue("order", request.DisplayOrder ?? oldOrder);
            updateCmd.Parameters.AddWithValue("id", categoryId);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        if (!string.Equals(oldName, request.CategoryName, StringComparison.OrdinalIgnoreCase))
        {
            await using var syncItemsCmd = new NpgsqlCommand(
                "UPDATE public.finished_furniture SET category = @newName WHERE lower(category) = lower(@oldName)",
                connection, transaction);
            syncItemsCmd.Parameters.AddWithValue("newName", request.CategoryName);
            syncItemsCmd.Parameters.AddWithValue("oldName", oldName);
            await syncItemsCmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);

        var all = await ListCategoriesAsync(ct);
        return new(Data: all.First(c => c.CategoryId == categoryId));
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> RequestRestockAsync(
        int actorId, string actorName, RequestWorkshopRestockRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        var item = await FindByIdInternalAsync(connection, null, request.ProductId, ct);
        if (item is null)
            return new(Error: FinishedFurnitureWriteError.NotFound, Message: "Furniture item not found.");

        var statusLabel = request.IsUrgent || item.CurrentStock <= 0
            ? "Urgent Restock Sent"
            : $"Job #{145 + (item.ProductId % 15)} Active";

        await using var cmd = new NpgsqlCommand("""
            UPDATE public.finished_furniture
            SET restock_requested = TRUE,
                restock_requested_qty = @qty,
                workshop_status_label = @status
            WHERE product_id = @id
            """, connection);
        cmd.Parameters.AddWithValue("qty", request.RestockQty);
        cmd.Parameters.AddWithValue("status", statusLabel);
        cmd.Parameters.AddWithValue("id", request.ProductId);
        await cmd.ExecuteNonQueryAsync(ct);

        var updated = await FindByIdInternalAsync(connection, null, request.ProductId, ct);
        return new(Data: updated);
    }

    public async Task<int> RequestBatchRestockAsync(int actorId, string actorName, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var cmd = new NpgsqlCommand("""
            UPDATE public.finished_furniture
            SET restock_requested = TRUE,
                restock_requested_qty = CASE
                    WHEN restock_requested_qty > 0 THEN restock_requested_qty
                    ELSE GREATEST(2, (min_reorder_level - current_stock) + 2)
                END,
                workshop_status_label = CASE
                    WHEN current_stock <= 0 THEN 'Urgent Restock Sent'
                    ELSE COALESCE(workshop_status_label, 'Sent to Workshop')
                END
            WHERE is_active IS TRUE AND current_stock <= min_reorder_level
            """, connection);

        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
