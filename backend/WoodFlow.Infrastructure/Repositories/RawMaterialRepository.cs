using System.Data;
using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class RawMaterialRepository(Database database) : IRawMaterialRepository
{
    private readonly SemaphoreSlim _schemaInitLock = new(1, 1);
    private bool _schemaInitialized;

    private async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        if (_schemaInitialized) return;
        await _schemaInitLock.WaitAsync(ct);
        try
        {
            if (_schemaInitialized) return;

            const string sql = """
                CREATE EXTENSION IF NOT EXISTS "pgcrypto";

                CREATE TABLE IF NOT EXISTS public.raw_materials (
                    material_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    material_code VARCHAR(30) UNIQUE NOT NULL,
                    name VARCHAR(120) NOT NULL,
                    category VARCHAR(50) NOT NULL,
                    unit_of_measure VARCHAR(20) NOT NULL,
                    quantity_on_hand NUMERIC(12,2) NOT NULL DEFAULT 0.00,
                    reorder_level NUMERIC(12,2) NOT NULL DEFAULT 10.00,
                    unit_cost NUMERIC(12,2) NOT NULL DEFAULT 0.00,
                    storage_location VARCHAR(80) NOT NULL DEFAULT 'Main Yard · Bay A',
                    notes TEXT DEFAULT '',
                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

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

                CREATE TABLE IF NOT EXISTS public.material_suppliers (
                    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
                    supplier_id UUID NOT NULL REFERENCES public.suppliers(supplier_id) ON DELETE CASCADE,
                    unit_price NUMERIC(12,2) NOT NULL DEFAULT 0.00,
                    lead_time_days INT NOT NULL DEFAULT 3,
                    is_preferred BOOLEAN NOT NULL DEFAULT FALSE,
                    PRIMARY KEY (material_id, supplier_id)
                );

                CREATE TABLE IF NOT EXISTS public.raw_material_receipts (
                    receipt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    grn_number VARCHAR(30) UNIQUE NOT NULL,
                    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
                    supplier_id UUID REFERENCES public.suppliers(supplier_id) ON DELETE SET NULL,
                    quantity_received NUMERIC(12,2) NOT NULL,
                    unit_cost NUMERIC(12,2) NOT NULL,
                    moisture_pct NUMERIC(5,2) DEFAULT 12.00,
                    received_by VARCHAR(100) NOT NULL DEFAULT 'Workshop Foreman',
                    notes TEXT DEFAULT '',
                    received_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS public.job_materials (
                    job_material_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    job_id VARCHAR(50) NOT NULL DEFAULT 'JOB-WORKSHOP-GEN',
                    material_id UUID NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
                    quantity_issued NUMERIC(12,2) NOT NULL,
                    artisan_name VARCHAR(100) NOT NULL DEFAULT 'Senior Artisan',
                    notes TEXT DEFAULT '',
                    issued_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS public.audit_logs (
                    log_id SERIAL PRIMARY KEY,
                    event_type VARCHAR(60) NOT NULL,
                    entity_name VARCHAR(60) NOT NULL,
                    entity_id VARCHAR(60),
                    details JSONB,
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

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
                """;

            await using (var cmd = new NpgsqlCommand(sql, connection))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SeedReferenceMaterialsIfMissingAsync(connection, ct);
            _schemaInitialized = true;
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }

    private static async Task SeedReferenceMaterialsIfMissingAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var checkCmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.raw_materials WHERE material_code = 'RAW-TIM-01')", connection);
        var exists = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (exists) return;

        const string seedSql = """
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

            INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred) VALUES
            ('b0000001-0000-0000-0000-000000000001', 'a0000001-0000-0000-0000-000000000001', 14500.00, 3, TRUE),
            ('b0000001-0000-0000-0000-000000000002', 'a0000001-0000-0000-0000-000000000001', 9200.00, 3, TRUE),
            ('b0000001-0000-0000-0000-000000000003', 'a0000001-0000-0000-0000-000000000005', 11500.00, 4, TRUE),
            ('b0000001-0000-0000-0000-000000000004', 'a0000001-0000-0000-0000-000000000004', 8400.00, 2, TRUE),
            ('b0000001-0000-0000-0000-000000000005', 'a0000001-0000-0000-0000-000000000002', 1250.00, 2, TRUE),
            ('b0000001-0000-0000-0000-000000000006', 'a0000001-0000-0000-0000-000000000003', 6800.00, 3, TRUE),
            ('b0000001-0000-0000-0000-000000000008', 'a0000001-0000-0000-0000-000000000003', 2100.00, 2, TRUE)
            ON CONFLICT (material_id, supplier_id) DO NOTHING;
            """;

        await using var seedCmd = new NpgsqlCommand(seedSql, connection);
        await seedCmd.ExecuteNonQueryAsync(ct);
    }

    private static RawMaterial ReadMaterial(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        r.GetDecimal(5),
        r.GetDecimal(6),
        r.GetDecimal(7),
        r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.GetBoolean(10),
        r.GetFieldValue<DateTimeOffset>(11),
        r.GetFieldValue<DateTimeOffset>(12)
    );

    private const string MaterialSelect = """
        material_id, material_code, name, category, unit_of_measure,
        quantity_on_hand, reorder_level, unit_cost, storage_location,
        notes, is_active, updated_at, created_at
        """;

    public async Task<IReadOnlyList<RawMaterial>> ListMaterialsAsync(string? category, string? search, bool includeInactive, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        var sql = $"""
            SELECT {MaterialSelect}
            FROM public.raw_materials
            WHERE (@includeInactive OR is_active IS TRUE)
              AND (@category IS NULL OR @category = '' OR category ILIKE @category)
              AND (@search IS NULL OR @search = '' OR (material_code ILIKE @searchPattern OR name ILIKE @searchPattern))
            ORDER BY material_code ASC
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("includeInactive", includeInactive);
        cmd.Parameters.AddWithValue("category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("search", (object?)search ?? DBNull.Value);
        cmd.Parameters.AddWithValue("searchPattern", string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{search.Trim()}%");

        var list = new List<RawMaterial>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(ReadMaterial(r));
        }
        return list;
    }

    public async Task<RawMaterial?> FindByIdAsync(Guid materialId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        var sql = $"SELECT {MaterialSelect} FROM public.raw_materials WHERE material_id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", materialId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMaterial(r) : null;
    }

    public async Task<RawMaterial?> FindByCodeAsync(string materialCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        var sql = $"SELECT {MaterialSelect} FROM public.raw_materials WHERE material_code = @code";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("code", materialCode);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMaterial(r) : null;
    }

    public async Task<RawMaterial> CreateAsync(RawMaterial material, Guid? preferredSupplierId, int? leadTimeDays, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            const string insertSql = """
                INSERT INTO public.raw_materials (
                    material_id, material_code, name, category, unit_of_measure,
                    quantity_on_hand, reorder_level, unit_cost, storage_location, notes, is_active, updated_at, created_at
                ) VALUES (
                    @id, @code, @name, @cat, @uom,
                    @qty, @reorder, @cost, @loc, @notes, @active, @updated, @created
                );
                """;

            await using (var cmd = new NpgsqlCommand(insertSql, connection, tx))
            {
                cmd.Parameters.AddWithValue("id", material.MaterialId);
                cmd.Parameters.AddWithValue("code", material.MaterialCode);
                cmd.Parameters.AddWithValue("name", material.Name);
                cmd.Parameters.AddWithValue("cat", material.Category);
                cmd.Parameters.AddWithValue("uom", material.UnitOfMeasure);
                cmd.Parameters.AddWithValue("qty", material.QuantityOnHand);
                cmd.Parameters.AddWithValue("reorder", material.ReorderLevel);
                cmd.Parameters.AddWithValue("cost", material.UnitCost);
                cmd.Parameters.AddWithValue("loc", material.StorageLocation);
                cmd.Parameters.AddWithValue("notes", (object?)material.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("active", material.IsActive);
                cmd.Parameters.AddWithValue("updated", material.UpdatedAt);
                cmd.Parameters.AddWithValue("created", material.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            if (preferredSupplierId.HasValue)
            {
                const string mapSql = """
                    INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred)
                    VALUES (@mId, @sId, @cost, @lead, TRUE)
                    ON CONFLICT (material_id, supplier_id) DO UPDATE SET is_preferred = TRUE;
                    """;
                await using var mapCmd = new NpgsqlCommand(mapSql, connection, tx);
                mapCmd.Parameters.AddWithValue("mId", material.MaterialId);
                mapCmd.Parameters.AddWithValue("sId", preferredSupplierId.Value);
                mapCmd.Parameters.AddWithValue("cost", material.UnitCost);
                mapCmd.Parameters.AddWithValue("lead", leadTimeDays ?? 3);
                await mapCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return material;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<RawMaterial?> UpdateAsync(RawMaterial material, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        const string updateSql = """
            UPDATE public.raw_materials
            SET name = @name,
                category = @cat,
                unit_of_measure = @uom,
                reorder_level = @reorder,
                unit_cost = @cost,
                storage_location = @loc,
                notes = @notes,
                is_active = @active,
                updated_at = @updated
            WHERE material_id = @id;
            """;

        await using var cmd = new NpgsqlCommand(updateSql, connection);
        cmd.Parameters.AddWithValue("name", material.Name);
        cmd.Parameters.AddWithValue("cat", material.Category);
        cmd.Parameters.AddWithValue("uom", material.UnitOfMeasure);
        cmd.Parameters.AddWithValue("reorder", material.ReorderLevel);
        cmd.Parameters.AddWithValue("cost", material.UnitCost);
        cmd.Parameters.AddWithValue("loc", material.StorageLocation);
        cmd.Parameters.AddWithValue("notes", (object?)material.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("active", material.IsActive);
        cmd.Parameters.AddWithValue("updated", material.UpdatedAt);
        cmd.Parameters.AddWithValue("id", material.MaterialId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0 ? material : null;
    }

    public async Task<GoodsReceiptNote> RecordGoodsReceiptAsync(
        Guid materialId, Guid? supplierId, decimal quantityReceived, decimal unitCost, decimal moisturePct, string receivedBy, string? notes, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            // 1. Fetch current material
            var mat = await FindByIdAsync(materialId, ct)
                ?? throw new InvalidOperationException("Raw material not found.");

            // 2. Increment stock atomically
            const string incSql = """
                UPDATE public.raw_materials
                SET quantity_on_hand = quantity_on_hand + @qty,
                    unit_cost = @cost,
                    updated_at = CURRENT_TIMESTAMP
                WHERE material_id = @id
                RETURNING quantity_on_hand;
                """;

            await using (var incCmd = new NpgsqlCommand(incSql, connection, tx))
            {
                incCmd.Parameters.AddWithValue("qty", quantityReceived);
                incCmd.Parameters.AddWithValue("cost", unitCost);
                incCmd.Parameters.AddWithValue("id", materialId);
                await incCmd.ExecuteScalarAsync(ct);
            }

            // 3. Generate sequential GRN number
            const string countSql = "SELECT count(*) FROM public.raw_material_receipts";
            await using var countCmd = new NpgsqlCommand(countSql, connection, tx);
            var nextNum = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct)) + 1;
            var grnNumber = $"GRN-2026-{nextNum:D4}";

            // 4. Insert receipt
            var receiptId = Guid.NewGuid();
            var receivedAt = DateTimeOffset.UtcNow;

            const string insReceipt = """
                INSERT INTO public.raw_material_receipts (
                    receipt_id, grn_number, material_id, supplier_id,
                    quantity_received, unit_cost, moisture_pct, received_by, notes, received_at
                ) VALUES (
                    @rId, @grn, @mId, @sId,
                    @qty, @cost, @moist, @recBy, @notes, @recAt
                );
                """;

            await using (var insCmd = new NpgsqlCommand(insReceipt, connection, tx))
            {
                insCmd.Parameters.AddWithValue("rId", receiptId);
                insCmd.Parameters.AddWithValue("grn", grnNumber);
                insCmd.Parameters.AddWithValue("mId", materialId);
                insCmd.Parameters.AddWithValue("sId", (object?)supplierId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("qty", quantityReceived);
                insCmd.Parameters.AddWithValue("cost", unitCost);
                insCmd.Parameters.AddWithValue("moist", moisturePct);
                insCmd.Parameters.AddWithValue("recBy", receivedBy);
                insCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("recAt", receivedAt);
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            // Fetch supplier name if present
            string? supplierName = null;
            if (supplierId.HasValue)
            {
                await using var supCmd = new NpgsqlCommand("SELECT company_name FROM public.suppliers WHERE supplier_id = @id", connection, tx);
                supCmd.Parameters.AddWithValue("id", supplierId.Value);
                supplierName = (string?)await supCmd.ExecuteScalarAsync(ct);
            }

            await tx.CommitAsync(ct);

            return new GoodsReceiptNote(
                receiptId,
                grnNumber,
                materialId,
                mat.MaterialCode,
                mat.Name,
                supplierId,
                supplierName,
                quantityReceived,
                mat.UnitOfMeasure,
                unitCost,
                moisturePct,
                receivedBy,
                notes,
                receivedAt
            );
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<GoodsReceiptNote>> ListGoodsReceiptsAsync(int limit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                r.receipt_id, r.grn_number, r.material_id, m.material_code, m.name,
                r.supplier_id, s.company_name, r.quantity_received, m.unit_of_measure,
                r.unit_cost, r.moisture_pct, r.received_by, r.notes, r.received_at
            FROM public.raw_material_receipts r
            JOIN public.raw_materials m ON r.material_id = m.material_id
            LEFT JOIN public.suppliers s ON r.supplier_id = s.supplier_id
            ORDER BY r.received_at DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<GoodsReceiptNote>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GoodsReceiptNote(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetString(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetFieldValue<DateTimeOffset>(13)
            ));
        }
        return list;
    }

    public async Task<JobMaterialConsumption> RecordConsumptionAsync(
        string jobId, Guid materialId, decimal quantityIssued, string artisanName, string? notes, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            // 1. Fetch current material and lock row
            const string lockSql = "SELECT quantity_on_hand, reorder_level, unit_of_measure, material_code, name FROM public.raw_materials WHERE material_id = @id FOR UPDATE";
            await using var lockCmd = new NpgsqlCommand(lockSql, connection, tx);
            lockCmd.Parameters.AddWithValue("id", materialId);

            decimal currentOnHand;
            decimal reorderLevel;
            string uom;
            string code;
            string name;

            await using (var r = await lockCmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("Raw material not found.");
                currentOnHand = r.GetDecimal(0);
                reorderLevel = r.GetDecimal(1);
                uom = r.GetString(2);
                code = r.GetString(3);
                name = r.GetString(4);
            }

            if (currentOnHand < quantityIssued)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for {code} ({name}). Available: {currentOnHand} {uom}, Requested: {quantityIssued} {uom}.");
            }

            var newOnHand = currentOnHand - quantityIssued;

            // 2. Decrement stock
            const string decSql = """
                UPDATE public.raw_materials
                SET quantity_on_hand = @newQty,
                    updated_at = CURRENT_TIMESTAMP
                WHERE material_id = @id;
                """;

            await using (var decCmd = new NpgsqlCommand(decSql, connection, tx))
            {
                decCmd.Parameters.AddWithValue("newQty", newOnHand);
                decCmd.Parameters.AddWithValue("id", materialId);
                await decCmd.ExecuteNonQueryAsync(ct);
            }

            // 3. Insert consumption
            var consId = Guid.NewGuid();
            var issuedAt = DateTimeOffset.UtcNow;

            const string insSql = """
                INSERT INTO public.job_materials (job_material_id, job_id, material_id, quantity_issued, artisan_name, notes, issued_at)
                VALUES (@id, @job, @mId, @qty, @artisan, @notes, @issued);
                """;

            await using (var insCmd = new NpgsqlCommand(insSql, connection, tx))
            {
                insCmd.Parameters.AddWithValue("id", consId);
                insCmd.Parameters.AddWithValue("job", jobId);
                insCmd.Parameters.AddWithValue("mId", materialId);
                insCmd.Parameters.AddWithValue("qty", quantityIssued);
                insCmd.Parameters.AddWithValue("artisan", artisanName);
                insCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("issued", issuedAt);
                await insCmd.ExecuteNonQueryAsync(ct);
            }

            string triggerImpact;
            if (newOnHand <= (reorderLevel * 0.50m))
                triggerImpact = "CRITICAL ALERT";
            else if (newOnHand <= reorderLevel)
                triggerImpact = "LOW TRIGGERED";
            else
                triggerImpact = "DEDUCTED";

            await tx.CommitAsync(ct);

            return new JobMaterialConsumption(
                consId,
                jobId,
                materialId,
                code,
                name,
                uom,
                quantityIssued,
                artisanName,
                notes,
                issuedAt,
                triggerImpact
            );
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<JobMaterialConsumption>> ListConsumptionsAsync(int limit, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                j.job_material_id, j.job_id, j.material_id, m.material_code, m.name,
                m.unit_of_measure, j.quantity_issued, j.artisan_name, j.notes, j.issued_at,
                CASE
                    WHEN m.quantity_on_hand <= (m.reorder_level * 0.50) THEN 'CRITICAL ALERT'
                    WHEN m.quantity_on_hand <= m.reorder_level THEN 'LOW TRIGGERED'
                    ELSE 'DEDUCTED'
                END AS trigger_impact
            FROM public.job_materials j
            JOIN public.raw_materials m ON j.material_id = m.material_id
            ORDER BY j.issued_at DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<JobMaterialConsumption>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new JobMaterialConsumption(
                r.GetGuid(0),
                r.GetString(1),
                r.GetGuid(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.GetDecimal(6),
                r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.GetFieldValue<DateTimeOffset>(9),
                r.GetString(10)
            ));
        }
        return list;
    }

    public async Task<IReadOnlyList<LowStockReorderItem>> ListLowStockReorderQueueAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                m.material_id, m.material_code, m.name, m.category, m.unit_of_measure,
                m.quantity_on_hand, m.reorder_level,
                GREATEST(0, m.reorder_level - m.quantity_on_hand) AS deficit_quantity,
                ROUND(GREATEST(0, (m.reorder_level * 1.50) - m.quantity_on_hand), 2) AS suggested_order_qty,
                m.unit_cost,
                s.company_name, s.phone, ms.lead_time_days,
                CASE
                    WHEN m.quantity_on_hand <= (m.reorder_level * 0.50) THEN 'CRITICAL'
                    ELSE 'LOW'
                END AS severity
            FROM public.raw_materials m
            LEFT JOIN public.material_suppliers ms ON m.material_id = ms.material_id AND ms.is_preferred IS TRUE
            LEFT JOIN public.suppliers s ON ms.supplier_id = s.supplier_id
            WHERE m.quantity_on_hand <= m.reorder_level AND m.is_active IS TRUE
            ORDER BY (m.quantity_on_hand / NULLIF(m.reorder_level, 0)) ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var list = new List<LowStockReorderItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new LowStockReorderItem(
                r.GetGuid(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetDecimal(5),
                r.GetDecimal(6),
                r.GetDecimal(7),
                r.GetDecimal(8),
                r.GetDecimal(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetString(11),
                r.IsDBNull(12) ? null : r.GetInt32(12),
                r.GetString(13)
            ));
        }
        return list;
    }

    public async Task<RawMaterialStockSummary> GetStockSummaryAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                COUNT(*)::int AS total_skus,
                COALESCE(SUM(quantity_on_hand * unit_cost), 0)::numeric(14,2) AS total_val,
                COALESCE(SUM(CASE WHEN quantity_on_hand <= reorder_level THEN 1 ELSE 0 END), 0)::int AS low_stock,
                COALESCE(SUM(CASE WHEN quantity_on_hand <= (reorder_level * 0.50) THEN 1 ELSE 0 END), 0)::int AS critical_stock,
                (SELECT COUNT(*)::int FROM public.suppliers WHERE is_active IS TRUE) AS active_suppliers
            FROM public.raw_materials
            WHERE is_active IS TRUE;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
        {
            return new RawMaterialStockSummary(
                r.GetInt32(0),
                r.GetDecimal(1),
                r.GetInt32(2),
                r.GetInt32(3),
                r.GetInt32(4)
            );
        }
        return new RawMaterialStockSummary(0, 0, 0, 0, 0);
    }
}
