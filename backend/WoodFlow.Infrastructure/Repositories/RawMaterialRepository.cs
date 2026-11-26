using Npgsql;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class RawMaterialRepository(Database database) : IRawMaterialRepository
{
    private static readonly SemaphoreSlim SchemaInitLock = new(1, 1);
    private static volatile bool _schemaInitialized;

    internal static async Task EnsureExtendedSchemaAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        if (_schemaInitialized) return;
        await SchemaInitLock.WaitAsync(ct);
        try
        {
            if (_schemaInitialized) return;

            const string sql = """
                ALTER TABLE public.raw_materials
                    ALTER COLUMN material_name TYPE VARCHAR(120);

                ALTER TABLE public.raw_materials
                    ADD COLUMN IF NOT EXISTS category VARCHAR(50) NOT NULL DEFAULT 'Timber',
                    ADD COLUMN IF NOT EXISTS storage_location VARCHAR(80) NOT NULL DEFAULT 'Main Yard · Bay A',
                    ADD COLUMN IF NOT EXISTS notes TEXT DEFAULT '',
                    ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP;

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

                CREATE TABLE IF NOT EXISTS public.material_suppliers (
                    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
                    supplier_id INT NOT NULL REFERENCES public.suppliers(supplier_id) ON DELETE CASCADE,
                    unit_price NUMERIC(12,2) NOT NULL DEFAULT 0.00,
                    lead_time_days INT NOT NULL DEFAULT 3,
                    is_preferred BOOLEAN NOT NULL DEFAULT FALSE,
                    PRIMARY KEY (material_id, supplier_id)
                );

                CREATE TABLE IF NOT EXISTS public.raw_material_receipts (
                    receipt_id SERIAL PRIMARY KEY,
                    grn_number VARCHAR(30) UNIQUE NOT NULL,
                    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
                    supplier_id INT REFERENCES public.suppliers(supplier_id) ON DELETE SET NULL,
                    quantity_received NUMERIC(12,2) NOT NULL,
                    unit_cost NUMERIC(12,2) NOT NULL,
                    moisture_pct NUMERIC(5,2) NOT NULL DEFAULT 12.00,
                    received_by VARCHAR(100) NOT NULL DEFAULT 'Workshop Foreman',
                    notes TEXT DEFAULT '',
                    received_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS public.raw_material_consumptions (
                    consumption_id SERIAL PRIMARY KEY,
                    job_reference VARCHAR(50) NOT NULL DEFAULT 'JOB-WORKSHOP-GEN',
                    production_job_id INT REFERENCES public.production_jobs(job_id) ON DELETE SET NULL,
                    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE RESTRICT,
                    quantity_issued NUMERIC(12,2) NOT NULL,
                    unit_cost_at_time NUMERIC(12,2) NOT NULL DEFAULT 0.00,
                    artisan_name VARCHAR(100) NOT NULL DEFAULT 'Senior Artisan',
                    notes TEXT DEFAULT '',
                    issued_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS public.raw_material_stock_alerts (
                    alert_id BIGSERIAL PRIMARY KEY,
                    material_id INT NOT NULL REFERENCES public.raw_materials(material_id) ON DELETE CASCADE,
                    alert_type VARCHAR(30) NOT NULL,
                    material_code VARCHAR(30) NOT NULL,
                    material_name VARCHAR(120) NOT NULL,
                    current_stock NUMERIC(12,2) NOT NULL,
                    reorder_level NUMERIC(12,2) NOT NULL,
                    unit_of_measure VARCHAR(20) NOT NULL,
                    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

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
            SchemaInitLock.Release();
        }
    }

    private static async Task SeedReferenceMaterialsIfMissingAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var checkCmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.suppliers WHERE supplier_code = 'SUP-MOR-01')", connection);
        var exists = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (exists) return;

        const string seedSql = """
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
            """;

        await using var seedCmd = new NpgsqlCommand(seedSql, connection);
        await seedCmd.ExecuteNonQueryAsync(ct);
    }

    private static RawMaterial ReadMaterial(NpgsqlDataReader r) => new(
        r.GetInt32(0),
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
        material_id, material_code, material_name, category, unit_of_measure,
        current_stock, reorder_level, unit_cost, storage_location,
        notes, is_active, updated_at, created_at
        """;

    public async Task<IReadOnlyList<RawMaterial>> ListMaterialsAsync(string? category, string? search, bool includeInactive, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        var sql = $"""
            SELECT {MaterialSelect}
            FROM public.raw_materials
            WHERE (@includeInactive OR is_active IS TRUE)
              AND (@category IS NULL OR @category = '' OR category ILIKE @category)
              AND (@search IS NULL OR @search = '' OR (material_code ILIKE @searchPattern OR material_name ILIKE @searchPattern))
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

    public async Task<RawMaterial?> FindByIdAsync(int materialId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        var sql = $"SELECT {MaterialSelect} FROM public.raw_materials WHERE material_id = @id";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", materialId);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMaterial(r) : null;
    }

    public async Task<RawMaterial?> FindByCodeAsync(string materialCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        var sql = $"SELECT {MaterialSelect} FROM public.raw_materials WHERE material_code = @code";
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("code", materialCode);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadMaterial(r) : null;
    }

    public async Task<RawMaterial> CreateAsync(RawMaterial material, int? preferredSupplierId, int? leadTimeDays, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            const string insertSql = """
                INSERT INTO public.raw_materials (
                    material_code, material_name, category, unit_of_measure,
                    current_stock, reorder_level, unit_cost, storage_location, notes, is_active, updated_at, created_at
                ) VALUES (
                    @code, @name, @cat, @uom,
                    @qty, @reorder, @cost, @loc, @notes, @active, @updated, @created
                )
                RETURNING material_id;
                """;

            int newId;
            await using (var cmd = new NpgsqlCommand(insertSql, connection, tx))
            {
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
                newId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            }

            if (preferredSupplierId.HasValue)
            {
                const string mapSql = """
                    INSERT INTO public.material_suppliers (material_id, supplier_id, unit_price, lead_time_days, is_preferred)
                    VALUES (@mId, @sId, @cost, @lead, TRUE)
                    ON CONFLICT (material_id, supplier_id) DO UPDATE SET is_preferred = TRUE;
                    """;
                await using var mapCmd = new NpgsqlCommand(mapSql, connection, tx);
                mapCmd.Parameters.AddWithValue("mId", newId);
                mapCmd.Parameters.AddWithValue("sId", preferredSupplierId.Value);
                mapCmd.Parameters.AddWithValue("cost", material.UnitCost);
                mapCmd.Parameters.AddWithValue("lead", leadTimeDays ?? 3);
                await mapCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return material with { MaterialId = newId };
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
        await EnsureExtendedSchemaAsync(connection, ct);

        const string updateSql = """
            UPDATE public.raw_materials
            SET material_name = @name,
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
        int materialId, int? supplierId, decimal quantityReceived, decimal unitCost, decimal moisturePct, string receivedBy, string? notes, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            string materialCode;
            string materialName;
            string uom;

            const string lockSql = "SELECT material_code, material_name, unit_of_measure FROM public.raw_materials WHERE material_id = @id FOR UPDATE";
            await using (var lockCmd = new NpgsqlCommand(lockSql, connection, tx))
            {
                lockCmd.Parameters.AddWithValue("id", materialId);
                await using var r = await lockCmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("Raw material not found.");
                materialCode = r.GetString(0);
                materialName = r.GetString(1);
                uom = r.GetString(2);
            }

            const string incSql = """
                UPDATE public.raw_materials
                SET current_stock = current_stock + @qty,
                    unit_cost = @cost,
                    updated_at = CURRENT_TIMESTAMP
                WHERE material_id = @id;
                """;

            await using (var incCmd = new NpgsqlCommand(incSql, connection, tx))
            {
                incCmd.Parameters.AddWithValue("qty", quantityReceived);
                incCmd.Parameters.AddWithValue("cost", unitCost);
                incCmd.Parameters.AddWithValue("id", materialId);
                await incCmd.ExecuteNonQueryAsync(ct);
            }

            const string countSql = "SELECT COALESCE(MAX(receipt_id), 0) + 1 FROM public.raw_material_receipts";
            await using var countCmd = new NpgsqlCommand(countSql, connection, tx);
            var nextNum = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
            var grnNumber = $"GRN-2026-{nextNum:D4}";
            var receivedAt = DateTimeOffset.UtcNow;

            const string insReceipt = """
                INSERT INTO public.raw_material_receipts (
                    grn_number, material_id, supplier_id,
                    quantity_received, unit_cost, moisture_pct, received_by, notes, received_at
                ) VALUES (
                    @grn, @mId, @sId,
                    @qty, @cost, @moist, @recBy, @notes, @recAt
                )
                RETURNING receipt_id;
                """;

            int receiptId;
            await using (var insCmd = new NpgsqlCommand(insReceipt, connection, tx))
            {
                insCmd.Parameters.AddWithValue("grn", grnNumber);
                insCmd.Parameters.AddWithValue("mId", materialId);
                insCmd.Parameters.AddWithValue("sId", (object?)supplierId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("qty", quantityReceived);
                insCmd.Parameters.AddWithValue("cost", unitCost);
                insCmd.Parameters.AddWithValue("moist", moisturePct);
                insCmd.Parameters.AddWithValue("recBy", receivedBy);
                insCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("recAt", receivedAt);
                receiptId = Convert.ToInt32(await insCmd.ExecuteScalarAsync(ct));
            }

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
                materialCode,
                materialName,
                supplierId,
                supplierName,
                quantityReceived,
                uom,
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
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                r.receipt_id, r.grn_number, r.material_id, m.material_code, m.material_name,
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
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
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
        string jobId, int materialId, decimal quantityIssued, string artisanName, string? notes, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            const string lockSql = "SELECT current_stock, reorder_level, unit_of_measure, material_code, material_name, unit_cost FROM public.raw_materials WHERE material_id = @id FOR UPDATE";
            await using var lockCmd = new NpgsqlCommand(lockSql, connection, tx);
            lockCmd.Parameters.AddWithValue("id", materialId);

            decimal currentOnHand;
            decimal reorderLevel;
            string uom;
            string code;
            string name;
            decimal unitCost;

            await using (var r = await lockCmd.ExecuteReaderAsync(ct))
            {
                if (!await r.ReadAsync(ct))
                    throw new InvalidOperationException("Raw material not found.");
                currentOnHand = r.GetDecimal(0);
                reorderLevel = r.GetDecimal(1);
                uom = r.GetString(2);
                code = r.GetString(3);
                name = r.GetString(4);
                unitCost = r.GetDecimal(5);
            }

            if (currentOnHand < quantityIssued)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for {code} ({name}). Available: {currentOnHand} {uom}, Requested: {quantityIssued} {uom}.");
            }

            var newOnHand = currentOnHand - quantityIssued;

            const string decSql = """
                UPDATE public.raw_materials
                SET current_stock = @newQty,
                    updated_at = CURRENT_TIMESTAMP
                WHERE material_id = @id;
                """;

            await using (var decCmd = new NpgsqlCommand(decSql, connection, tx))
            {
                decCmd.Parameters.AddWithValue("newQty", newOnHand);
                decCmd.Parameters.AddWithValue("id", materialId);
                await decCmd.ExecuteNonQueryAsync(ct);
            }

            var issuedAt = DateTimeOffset.UtcNow;

            const string insSql = """
                INSERT INTO public.raw_material_consumptions (
                    job_reference, material_id, quantity_issued, unit_cost_at_time, artisan_name, notes, issued_at
                ) VALUES (
                    @job, @mId, @qty, @cost, @artisan, @notes, @issued
                )
                RETURNING consumption_id;
                """;

            int consId;
            await using (var insCmd = new NpgsqlCommand(insSql, connection, tx))
            {
                insCmd.Parameters.AddWithValue("job", jobId);
                insCmd.Parameters.AddWithValue("mId", materialId);
                insCmd.Parameters.AddWithValue("qty", quantityIssued);
                insCmd.Parameters.AddWithValue("cost", unitCost);
                insCmd.Parameters.AddWithValue("artisan", artisanName);
                insCmd.Parameters.AddWithValue("notes", (object?)notes ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("issued", issuedAt);
                consId = Convert.ToInt32(await insCmd.ExecuteScalarAsync(ct));
            }

            var triggerImpact = newOnHand <= (reorderLevel * 0.50m)
                ? "CRITICAL ALERT"
                : newOnHand <= reorderLevel
                    ? "LOW TRIGGERED"
                    : "DEDUCTED";

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
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                c.consumption_id, c.job_reference, c.material_id, m.material_code, m.material_name,
                m.unit_of_measure, c.quantity_issued, c.artisan_name, c.notes, c.issued_at,
                CASE
                    WHEN m.current_stock <= (m.reorder_level * 0.50) THEN 'CRITICAL ALERT'
                    WHEN m.current_stock <= m.reorder_level THEN 'LOW TRIGGERED'
                    ELSE 'DEDUCTED'
                END AS trigger_impact
            FROM public.raw_material_consumptions c
            JOIN public.raw_materials m ON c.material_id = m.material_id
            ORDER BY c.issued_at DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("limit", limit);

        var list = new List<JobMaterialConsumption>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new JobMaterialConsumption(
                r.GetInt32(0),
                r.GetString(1),
                r.GetInt32(2),
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
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                m.material_id, m.material_code, m.material_name, m.category, m.unit_of_measure,
                m.current_stock, m.reorder_level,
                GREATEST(0, m.reorder_level - m.current_stock) AS deficit_quantity,
                ROUND(GREATEST(0, (m.reorder_level * 1.50) - m.current_stock), 2) AS suggested_order_qty,
                m.unit_cost,
                s.company_name, s.phone, ms.lead_time_days,
                CASE
                    WHEN m.current_stock <= (m.reorder_level * 0.50) THEN 'CRITICAL'
                    ELSE 'LOW'
                END AS severity
            FROM public.raw_materials m
            LEFT JOIN public.material_suppliers ms ON m.material_id = ms.material_id AND ms.is_preferred IS TRUE
            LEFT JOIN public.suppliers s ON ms.supplier_id = s.supplier_id
            WHERE m.current_stock <= m.reorder_level AND m.is_active IS TRUE
            ORDER BY (m.current_stock / NULLIF(m.reorder_level, 0)) ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        var list = new List<LowStockReorderItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new LowStockReorderItem(
                r.GetInt32(0),
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
        await EnsureExtendedSchemaAsync(connection, ct);

        const string sql = """
            SELECT
                COUNT(*)::int AS total_skus,
                COALESCE(SUM(current_stock * unit_cost), 0)::numeric(14,2) AS total_val,
                COALESCE(SUM(CASE WHEN current_stock <= reorder_level THEN 1 ELSE 0 END), 0)::int AS low_stock,
                COALESCE(SUM(CASE WHEN current_stock <= (reorder_level * 0.50) THEN 1 ELSE 0 END), 0)::int AS critical_stock,
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
