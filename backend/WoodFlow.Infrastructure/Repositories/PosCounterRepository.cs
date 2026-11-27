using System.Text.Json;
using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class PosCounterRepository(Database database) : IPosCounterRepository
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
                ALTER TABLE public.finished_furniture
                    ADD COLUMN IF NOT EXISTS barcode VARCHAR(30),
                    ADD COLUMN IF NOT EXISTS min_reorder_level INT NOT NULL DEFAULT 3,
                    ADD COLUMN IF NOT EXISTS image_key VARCHAR(40) NOT NULL DEFAULT 'lounge';

                ALTER TABLE public.showroom_stock_movements
                    DROP CONSTRAINT IF EXISTS showroom_stock_movements_movement_type_check;
                ALTER TABLE public.showroom_stock_movements
                    ADD CONSTRAINT showroom_stock_movements_movement_type_check
                    CHECK (movement_type IN ('SHOWROOM_INTAKE', 'POS_SALE', 'WORKSHOP_COMPLETED', 'STOCK_CORRECTION', 'CUSTOM_DELIVERY', 'SHOWROOM_RETURN'));

                ALTER TABLE public.showroom_invoices
                    ADD COLUMN IF NOT EXISTS subtotal_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    ADD COLUMN IF NOT EXISTS discount_code VARCHAR(40),
                    ADD COLUMN IF NOT EXISTS discount_percent DECIMAL(5,2) NOT NULL DEFAULT 0.00,
                    ADD COLUMN IF NOT EXISTS discount_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    ADD COLUMN IF NOT EXISTS cash_received DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    ADD COLUMN IF NOT EXISTS change_due DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    ADD COLUMN IF NOT EXISTS payment_reference VARCHAR(60),
                    ADD COLUMN IF NOT EXISTS invoice_status VARCHAR(20) NOT NULL DEFAULT 'PAID',
                    ADD COLUMN IF NOT EXISTS is_custom_order_deposit BOOLEAN NOT NULL DEFAULT FALSE,
                    ADD COLUMN IF NOT EXISTS till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
                    ADD COLUMN IF NOT EXISTS billed_by_name VARCHAR(100) NOT NULL DEFAULT 'Yohan Methusael E J',
                    ADD COLUMN IF NOT EXISTS void_reason TEXT,
                    ADD COLUMN IF NOT EXISTS voided_by INT REFERENCES public.users(user_id) ON DELETE SET NULL,
                    ADD COLUMN IF NOT EXISTS voided_at TIMESTAMP WITH TIME ZONE;

                ALTER TABLE public.showroom_invoice_items
                    ADD COLUMN IF NOT EXISTS item_name_snapshot VARCHAR(120),
                    ADD COLUMN IF NOT EXISTS sku_code_snapshot VARCHAR(30),
                    ADD COLUMN IF NOT EXISTS is_refunded BOOLEAN NOT NULL DEFAULT FALSE;

                CREATE TABLE IF NOT EXISTS public.cash_counter_transactions (
                    transaction_id BIGSERIAL PRIMARY KEY,
                    invoice_id INT REFERENCES public.showroom_invoices(invoice_id) ON DELETE SET NULL,
                    till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
                    transaction_type VARCHAR(30) NOT NULL,
                    payment_mode VARCHAR(20) NOT NULL,
                    amount DECIMAL(12,2) NOT NULL,
                    cash_received DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    change_due DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    reference_code VARCHAR(60),
                    recorded_by INT REFERENCES public.users(user_id) ON DELETE SET NULL,
                    recorded_by_name VARCHAR(100) NOT NULL DEFAULT 'Yohan Methusael E J',
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS public.pos_till_sessions (
                    session_id SERIAL PRIMARY KEY,
                    session_date DATE NOT NULL UNIQUE,
                    till_number VARCHAR(20) NOT NULL DEFAULT 'Till #01',
                    opening_float DECIMAL(12,2) NOT NULL DEFAULT 25000.00,
                    opened_at VARCHAR(20) NOT NULL DEFAULT '08:30 AM',
                    notes_5000_count INT NOT NULL DEFAULT 26,
                    notes_1000_count INT NOT NULL DEFAULT 38,
                    notes_500_count INT NOT NULL DEFAULT 8,
                    notes_100_count INT NOT NULL DEFAULT 12,
                    coins_50_count INT NOT NULL DEFAULT 6,
                    counted_cash_amount DECIMAL(12,2) NOT NULL DEFAULT 173500.00,
                    expected_cash_amount DECIMAL(12,2) NOT NULL DEFAULT 173500.00,
                    variance_amount DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    gross_day_revenue DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    safe_deposit_cash DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                    session_status VARCHAR(20) NOT NULL DEFAULT 'OPEN',
                    z_report_number VARCHAR(40),
                    closed_by_name VARCHAR(100),
                    verified_by_name VARCHAR(100),
                    closed_at TIMESTAMP WITH TIME ZONE
                );
                """;

            await using (var cmd = new NpgsqlCommand(sql, connection))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SeedCustomersAndPosShowcaseIfEmptyAsync(connection, ct);
            _schemaInitialized = true;
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }

    private static async Task SeedCustomersAndPosShowcaseIfEmptyAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        const string seedCustomers = """
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

            INSERT INTO public.finished_furniture (sku_code, barcode, item_name, category, retail_price, current_stock, min_reorder_level, image_key, is_active)
            VALUES ('FF-WAR-003', '8901245002024', 'Royal Arch 3-Door Teak Wardrobe', 'Bedroom', 145000.00, 0, 3, 'wardrobe', TRUE)
            ON CONFLICT (sku_code) DO NOTHING;
            """;

        await using var custCmd = new NpgsqlCommand(seedCustomers, connection);
        await custCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ShowroomPosProduct>> ListProductsAsync(string? category, string? search, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT
                product_id,
                sku_code,
                COALESCE(barcode, '890124500' || LPAD(product_id::text, 4, '0')) AS barcode,
                item_name,
                category,
                retail_price,
                current_stock,
                COALESCE(min_reorder_level, 3) AS min_reorder_level,
                COALESCE(image_key, 'lounge') AS image_key,
                COALESCE(is_active, TRUE) AS is_active
            FROM public.finished_furniture
            WHERE COALESCE(is_active, TRUE) = TRUE
            ORDER BY
                CASE WHEN current_stock > 0 THEN 0 ELSE 1 END,
                product_id ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ShowroomPosProduct>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ShowroomPosProduct(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetBoolean(9)));
        }

        return list;
    }

    public async Task<ShowroomPosProduct?> GetProductByIdAsync(int productId, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT
                product_id,
                sku_code,
                COALESCE(barcode, '890124500' || LPAD(product_id::text, 4, '0')) AS barcode,
                item_name,
                category,
                retail_price,
                current_stock,
                COALESCE(min_reorder_level, 3) AS min_reorder_level,
                COALESCE(image_key, 'lounge') AS image_key,
                COALESCE(is_active, TRUE) AS is_active
            FROM public.finished_furniture
            WHERE product_id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", productId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ShowroomPosProduct(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDecimal(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetBoolean(9));
    }

    public async Task<IReadOnlyList<PosCustomerSummary>> ListCustomersAsync(string? search, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT customer_id, full_name, phone_number, whatsapp_number, delivery_address, created_at
            FROM public.customers
            WHERE (@search IS NULL OR full_name ILIKE '%' || @search || '%' OR phone_number ILIKE '%' || @search || '%')
            ORDER BY customer_id DESC
            LIMIT 50;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PosCustomerSummary>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PosCustomerSummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return list;
    }

    public async Task<PosCustomerSummary?> GetCustomerByIdAsync(int customerId, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT customer_id, full_name, phone_number, whatsapp_number, delivery_address, created_at
            FROM public.customers
            WHERE customer_id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", customerId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new PosCustomerSummary(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task<PosWriteResult<PosCustomerSummary>> CreateCustomerAsync(CreatePosCustomerRequest request, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            INSERT INTO public.customers (full_name, phone_number, whatsapp_number, delivery_address)
            VALUES (@name, @phone, @whatsapp, @address)
            RETURNING customer_id, full_name, phone_number, whatsapp_number, delivery_address, created_at;
            """;

        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", request.FullName);
            cmd.Parameters.AddWithValue("phone", request.PhoneNumber);
            cmd.Parameters.AddWithValue("whatsapp", (object?)request.WhatsappNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("address", (object?)request.DeliveryAddress ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return new(new PosCustomerSummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new(Error: PosWriteError.DuplicatePhone, Message: $"A customer with phone number '{request.PhoneNumber}' already exists.");
        }
    }

    public async Task<IReadOnlyList<PosActiveOrderSummary>> ListActiveOrdersAsync(CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT
                co.order_number,
                COALESCE(c.full_name, 'Walk-in Customer') AS customer_name,
                co.item_description,
                co.agreed_price,
                co.deposit_paid,
                co.order_status
            FROM public.custom_orders co
            LEFT JOIN public.customers c ON c.customer_id = co.customer_id
            WHERE co.order_status NOT IN ('COMPLETED', 'CANCELLED')
            ORDER BY co.created_at DESC
            LIMIT 10;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PosActiveOrderSummary>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PosActiveOrderSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetString(5)));
        }
        return list;
    }

    public async Task<PosWriteResult<ShowroomInvoiceRecord>> CreateInvoiceAsync(
        int actorId,
        string actorName,
        PosComputedCheckout checkout,
        CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            int? validActorId = await ResolveExistingUserIdAsync(conn, tx, actorId, ct);

            await using var seqCmd = new NpgsqlCommand("SELECT COALESCE(MAX(invoice_id), 890) + 1 FROM public.showroom_invoices;", conn, tx);
            var nextSeq = Convert.ToInt32(await seqCmd.ExecuteScalarAsync(ct));
            var invoiceNumber = $"INV-{DateTime.UtcNow.AddHours(5.5):yyyy}-{Math.Max(nextSeq, 891):D4}";

            const string insertInvoiceSql = """
                INSERT INTO public.showroom_invoices (
                    invoice_number, customer_id, subtotal_amount, discount_code, discount_percent,
                    discount_amount, total_amount, payment_mode, payment_reference,
                    cash_received, change_due, invoice_status, is_custom_order_deposit,
                    till_number, billed_by, billed_by_name
                ) VALUES (
                    @invNo, @custId, @subtotal, @discCode, @discPct,
                    @discAmt, @total, @mode, @payRef,
                    @cashRecv, @changeDue, @status, @isDeposit,
                    @till, @billedBy, @billedByName
                )
                RETURNING invoice_id;
                """;

            int invoiceId;
            await using (var invCmd = new NpgsqlCommand(insertInvoiceSql, conn, tx))
            {
                invCmd.Parameters.AddWithValue("invNo", invoiceNumber);
                invCmd.Parameters.AddWithValue("custId", (object?)checkout.CustomerId ?? DBNull.Value);
                invCmd.Parameters.AddWithValue("subtotal", checkout.SubtotalAmount);
                invCmd.Parameters.AddWithValue("discCode", (object?)checkout.DiscountCode ?? DBNull.Value);
                invCmd.Parameters.AddWithValue("discPct", checkout.DiscountPercent);
                invCmd.Parameters.AddWithValue("discAmt", checkout.DiscountAmount);
                invCmd.Parameters.AddWithValue("total", checkout.TotalAmount);
                invCmd.Parameters.AddWithValue("mode", checkout.PaymentMode);
                invCmd.Parameters.AddWithValue("payRef", (object?)checkout.PaymentReference ?? DBNull.Value);
                invCmd.Parameters.AddWithValue("cashRecv", checkout.CashReceived);
                invCmd.Parameters.AddWithValue("changeDue", checkout.ChangeDue);
                invCmd.Parameters.AddWithValue("status", checkout.InvoiceStatus);
                invCmd.Parameters.AddWithValue("isDeposit", checkout.IsCustomOrderDeposit);
                invCmd.Parameters.AddWithValue("till", checkout.TillNumber);
                invCmd.Parameters.AddWithValue("billedBy", (object?)validActorId ?? DBNull.Value);
                invCmd.Parameters.AddWithValue("billedByName", actorName);
                invoiceId = Convert.ToInt32(await invCmd.ExecuteScalarAsync(ct));
            }

            const string insertLineSql = """
                INSERT INTO public.showroom_invoice_items (
                    invoice_id, product_id, quantity, unit_price, item_name_snapshot, sku_code_snapshot
                ) VALUES (
                    @invoiceId, @productId, @quantity, @unitPrice, @itemName, @skuCode
                );
                """;

            foreach (var line in checkout.Lines)
            {
                await using var lineCmd = new NpgsqlCommand(insertLineSql, conn, tx);
                lineCmd.Parameters.AddWithValue("invoiceId", invoiceId);
                lineCmd.Parameters.AddWithValue("productId", line.ProductId);
                lineCmd.Parameters.AddWithValue("quantity", line.Quantity);
                lineCmd.Parameters.AddWithValue("unitPrice", line.UnitPrice);
                lineCmd.Parameters.AddWithValue("itemName", line.ItemName);
                lineCmd.Parameters.AddWithValue("skuCode", line.SkuCode);
                await lineCmd.ExecuteNonQueryAsync(ct);
            }

            var txType = checkout.PaymentMode switch
            {
                PosPaymentModes.Card => "CARD_SALE",
                PosPaymentModes.BankTransfer => "BANK_TRANSFER_SALE",
                _ => "CASH_SALE"
            };

            const string insertLedgerSql = """
                INSERT INTO public.cash_counter_transactions (
                    invoice_id, till_number, transaction_type, payment_mode,
                    amount, cash_received, change_due, reference_code, recorded_by, recorded_by_name
                ) VALUES (
                    @invoiceId, @till, @txType, @mode,
                    @amount, @cashRecv, @changeDue, @refCode, @recordedBy, @recordedByName
                );
                """;

            await using (var ledgerCmd = new NpgsqlCommand(insertLedgerSql, conn, tx))
            {
                ledgerCmd.Parameters.AddWithValue("invoiceId", invoiceId);
                ledgerCmd.Parameters.AddWithValue("till", checkout.TillNumber);
                ledgerCmd.Parameters.AddWithValue("txType", txType);
                ledgerCmd.Parameters.AddWithValue("mode", checkout.PaymentMode);
                ledgerCmd.Parameters.AddWithValue("amount", checkout.TotalAmount);
                ledgerCmd.Parameters.AddWithValue("cashRecv", checkout.CashReceived);
                ledgerCmd.Parameters.AddWithValue("changeDue", checkout.ChangeDue);
                ledgerCmd.Parameters.AddWithValue("refCode", (object?)(checkout.PaymentReference ?? invoiceNumber) ?? DBNull.Value);
                ledgerCmd.Parameters.AddWithValue("recordedBy", (object?)validActorId ?? DBNull.Value);
                ledgerCmd.Parameters.AddWithValue("recordedByName", actorName);
                await ledgerCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            var created = await GetInvoiceByIdAsync(invoiceId, ct);
            return new(created);
        }
        catch (PostgresException ex) when (ex.MessageText.Contains("Insufficient showroom stock", StringComparison.OrdinalIgnoreCase))
        {
            await tx.RollbackAsync(ct);
            return new(
                Error: PosWriteError.InsufficientStock,
                Message: $"Database trigger trg_pos_stock_deduct aborted checkout: {ex.MessageText}");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ShowroomInvoiceRecord>> ListInvoicesAsync(
        string? search,
        string? paymentMode,
        DateOnly? date,
        CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT
                si.invoice_id,
                si.invoice_number,
                si.customer_id,
                COALESCE(c.full_name, 'Walk-in Customer') AS customer_name,
                COALESCE(c.phone_number, '+94 77 000 0000') AS customer_phone,
                c.delivery_address,
                COALESCE(NULLIF(si.subtotal_amount, 0), si.total_amount) AS subtotal_amount,
                si.discount_code,
                COALESCE(si.discount_percent, 0) AS discount_percent,
                COALESCE(si.discount_amount, 0) AS discount_amount,
                si.total_amount,
                si.payment_mode,
                si.payment_reference,
                COALESCE(NULLIF(si.cash_received, 0), si.total_amount) AS cash_received,
                COALESCE(si.change_due, 0) AS change_due,
                COALESCE(si.invoice_status, 'PAID') AS invoice_status,
                COALESCE(si.is_custom_order_deposit, FALSE) AS is_custom_order_deposit,
                COALESCE(si.till_number, 'Till #01') AS till_number,
                si.billed_by,
                COALESCE(u.full_name, si.billed_by_name, 'Yohan Methusael E J') AS billed_by_name,
                si.invoice_date,
                si.void_reason,
                si.voided_at
            FROM public.showroom_invoices si
            LEFT JOIN public.customers c ON c.customer_id = si.customer_id
            LEFT JOIN public.users u ON u.user_id = si.billed_by
            WHERE (@search IS NULL
                   OR si.invoice_number ILIKE '%' || @search || '%'
                   OR COALESCE(c.full_name, 'Walk-in Customer') ILIKE '%' || @search || '%'
                   OR COALESCE(c.phone_number, '') ILIKE '%' || @search || '%')
              AND (@mode IS NULL OR si.payment_mode = @mode)
              AND (@saleDate IS NULL OR DATE(si.invoice_date AT TIME ZONE 'Asia/Colombo') = @saleDate)
            ORDER BY si.invoice_date DESC, si.invoice_id DESC
            LIMIT 100;
            """;

        string? normalizedMode = string.IsNullOrWhiteSpace(paymentMode) || paymentMode.Equals("ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : paymentMode.Trim().ToUpperInvariant();

        var headers = new List<(
            int Id, string Number, int? CustId, string CustName, string CustPhone, string? CustAddr,
            decimal Subtotal, string? DiscCode, decimal DiscPct, decimal DiscAmt, decimal Total,
            string Mode, string? PayRef, decimal CashRecv, decimal Change, string Status,
            bool IsDeposit, string Till, int? BilledBy, string BilledByName, DateTimeOffset Date,
            string? VoidReason, DateTimeOffset? VoidedAt)>();

        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
            cmd.Parameters.AddWithValue("mode", (object?)normalizedMode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("saleDate", date.HasValue ? date.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                headers.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.GetDecimal(13),
                    reader.GetDecimal(14),
                    reader.GetString(15),
                    reader.GetBoolean(16),
                    reader.GetString(17),
                    reader.IsDBNull(18) ? null : reader.GetInt32(18),
                    reader.GetString(19),
                    reader.GetFieldValue<DateTimeOffset>(20),
                    reader.IsDBNull(21) ? null : reader.GetString(21),
                    reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset>(22)));
            }
        }

        if (headers.Count == 0) return [];

        var linesByInvoice = await LoadLinesByInvoiceIdsAsync(conn, headers.Select(h => h.Id).ToArray(), ct);
        return headers.Select(h => new ShowroomInvoiceRecord(
            h.Id, h.Number, h.CustId, h.CustName, h.CustPhone, h.CustAddr,
            h.Subtotal, h.DiscCode, h.DiscPct, h.DiscAmt, h.Total,
            h.Mode, h.PayRef, h.CashRecv, h.Change, h.Status,
            h.IsDeposit, h.Till, h.BilledBy, h.BilledByName, h.Date,
            h.VoidReason, h.VoidedAt,
            linesByInvoice.GetValueOrDefault(h.Id) ?? [])).ToList();
    }

    public async Task<ShowroomInvoiceRecord?> GetInvoiceByIdAsync(int invoiceId, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        const string sql = """
            SELECT
                si.invoice_id,
                si.invoice_number,
                si.customer_id,
                COALESCE(c.full_name, 'Walk-in Customer') AS customer_name,
                COALESCE(c.phone_number, '+94 77 000 0000') AS customer_phone,
                c.delivery_address,
                COALESCE(NULLIF(si.subtotal_amount, 0), si.total_amount) AS subtotal_amount,
                si.discount_code,
                COALESCE(si.discount_percent, 0) AS discount_percent,
                COALESCE(si.discount_amount, 0) AS discount_amount,
                si.total_amount,
                si.payment_mode,
                si.payment_reference,
                COALESCE(NULLIF(si.cash_received, 0), si.total_amount) AS cash_received,
                COALESCE(si.change_due, 0) AS change_due,
                COALESCE(si.invoice_status, 'PAID') AS invoice_status,
                COALESCE(si.is_custom_order_deposit, FALSE) AS is_custom_order_deposit,
                COALESCE(si.till_number, 'Till #01') AS till_number,
                si.billed_by,
                COALESCE(u.full_name, si.billed_by_name, 'Yohan Methusael E J') AS billed_by_name,
                si.invoice_date,
                si.void_reason,
                si.voided_at
            FROM public.showroom_invoices si
            LEFT JOIN public.customers c ON c.customer_id = si.customer_id
            LEFT JOIN public.users u ON u.user_id = si.billed_by
            WHERE si.invoice_id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", invoiceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var id = reader.GetInt32(0);
        var number = reader.GetString(1);
        var custId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
        var custName = reader.GetString(3);
        var custPhone = reader.GetString(4);
        var custAddr = reader.IsDBNull(5) ? null : reader.GetString(5);
        var subtotal = reader.GetDecimal(6);
        var discCode = reader.IsDBNull(7) ? null : reader.GetString(7);
        var discPct = reader.GetDecimal(8);
        var discAmt = reader.GetDecimal(9);
        var total = reader.GetDecimal(10);
        var mode = reader.GetString(11);
        var payRef = reader.IsDBNull(12) ? null : reader.GetString(12);
        var cashRecv = reader.GetDecimal(13);
        var change = reader.GetDecimal(14);
        var status = reader.GetString(15);
        var isDeposit = reader.GetBoolean(16);
        var till = reader.GetString(17);
        var billedBy = reader.IsDBNull(18) ? (int?)null : reader.GetInt32(18);
        var billedByName = reader.GetString(19);
        var date = reader.GetFieldValue<DateTimeOffset>(20);
        var voidReason = reader.IsDBNull(21) ? null : reader.GetString(21);
        var voidedAt = reader.IsDBNull(22) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(22);
        await reader.CloseAsync();

        var lines = await LoadLinesByInvoiceIdsAsync(conn, [id], ct);
        return new ShowroomInvoiceRecord(
            id, number, custId, custName, custPhone, custAddr,
            subtotal, discCode, discPct, discAmt, total,
            mode, payRef, cashRecv, change, status,
            isDeposit, till, billedBy, billedByName, date,
            voidReason, voidedAt, lines.GetValueOrDefault(id) ?? []);
    }

    public async Task<PosWriteResult<ShowroomInvoiceRecord>> VoidOrRefundInvoiceAsync(
        int actorId,
        string actorName,
        int invoiceId,
        string reason,
        IReadOnlyCollection<int>? itemIds,
        CancellationToken ct)
    {
        var existing = await GetInvoiceByIdAsync(invoiceId, ct);
        if (existing is null)
            return new(Error: PosWriteError.NotFound, Message: "Invoice not found.");

        if (existing.InvoiceStatus is PosInvoiceStatuses.Voided or PosInvoiceStatuses.Refunded)
            return new(Error: PosWriteError.AlreadyVoided, Message: $"Invoice #{existing.InvoiceNumber} has already been {existing.InvoiceStatus.ToLowerInvariant()}.");

        var targetLines = itemIds is { Count: > 0 }
            ? existing.Items.Where(i => itemIds.Contains(i.ItemId) && !i.IsRefunded).ToList()
            : existing.Items.Where(i => !i.IsRefunded).ToList();

        if (targetLines.Count == 0)
            return new(Error: PosWriteError.Validation, Message: "Select at least one non-refunded line item to void or return.");

        var isFullVoid = targetLines.Count == existing.Items.Count;
        var newStatus = isFullVoid ? PosInvoiceStatuses.Voided : PosInvoiceStatuses.Refunded;

        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            int? validActorId = await ResolveExistingUserIdAsync(conn, tx, actorId, ct);

            foreach (var line in targetLines)
            {
                await using var stockCmd = new NpgsqlCommand(
                    "SELECT current_stock FROM public.finished_furniture WHERE product_id = @pid FOR UPDATE;", conn, tx);
                stockCmd.Parameters.AddWithValue("pid", line.ProductId);
                var stockBefore = Convert.ToInt32(await stockCmd.ExecuteScalarAsync(ct));
                var stockAfter = stockBefore + line.Quantity;

                await using (var updStock = new NpgsqlCommand(
                    "UPDATE public.finished_furniture SET current_stock = @after WHERE product_id = @pid;", conn, tx))
                {
                    updStock.Parameters.AddWithValue("after", stockAfter);
                    updStock.Parameters.AddWithValue("pid", line.ProductId);
                    await updStock.ExecuteNonQueryAsync(ct);
                }

                await using (var movCmd = new NpgsqlCommand("""
                    INSERT INTO public.showroom_stock_movements (
                        product_id, movement_type, quantity_changed, stock_before, stock_after, reference_id, performed_by
                    ) VALUES (
                        @pid, 'SHOWROOM_RETURN', @qty, @before, @after, @refId, @actor
                    );
                    """, conn, tx))
                {
                    movCmd.Parameters.AddWithValue("pid", line.ProductId);
                    movCmd.Parameters.AddWithValue("qty", line.Quantity);
                    movCmd.Parameters.AddWithValue("before", stockBefore);
                    movCmd.Parameters.AddWithValue("after", stockAfter);
                    movCmd.Parameters.AddWithValue("refId", $"VOID-{existing.InvoiceNumber}");
                    movCmd.Parameters.AddWithValue("actor", (object?)validActorId ?? DBNull.Value);
                    await movCmd.ExecuteNonQueryAsync(ct);
                }

                await using (var markLine = new NpgsqlCommand(
                    "UPDATE public.showroom_invoice_items SET is_refunded = TRUE WHERE item_id = @itemId;", conn, tx))
                {
                    markLine.Parameters.AddWithValue("itemId", line.ItemId);
                    await markLine.ExecuteNonQueryAsync(ct);
                }
            }

            var refundRatio = existing.SubtotalAmount > 0m
                ? targetLines.Sum(l => l.Subtotal) / existing.SubtotalAmount
                : 1m;
            var refundAmount = isFullVoid
                ? existing.TotalAmount
                : Math.Round(existing.TotalAmount * refundRatio, 2, MidpointRounding.AwayFromZero);

            await using (var updInv = new NpgsqlCommand("""
                UPDATE public.showroom_invoices
                SET invoice_status = @status,
                    void_reason = @reason,
                    voided_by = @actor,
                    voided_at = CURRENT_TIMESTAMP
                WHERE invoice_id = @id;
                """, conn, tx))
            {
                updInv.Parameters.AddWithValue("status", newStatus);
                updInv.Parameters.AddWithValue("reason", reason);
                updInv.Parameters.AddWithValue("actor", (object?)validActorId ?? DBNull.Value);
                updInv.Parameters.AddWithValue("id", invoiceId);
                await updInv.ExecuteNonQueryAsync(ct);
            }

            await using (var ledgerCmd = new NpgsqlCommand("""
                INSERT INTO public.cash_counter_transactions (
                    invoice_id, till_number, transaction_type, payment_mode,
                    amount, reference_code, recorded_by, recorded_by_name
                ) VALUES (
                    @id, @till, 'VOID_REFUND', @mode,
                    @amount, @refCode, @actor, @actorName
                );
                """, conn, tx))
            {
                ledgerCmd.Parameters.AddWithValue("id", invoiceId);
                ledgerCmd.Parameters.AddWithValue("till", existing.TillNumber);
                ledgerCmd.Parameters.AddWithValue("mode", existing.PaymentMode);
                ledgerCmd.Parameters.AddWithValue("amount", -refundAmount);
                ledgerCmd.Parameters.AddWithValue("refCode", $"VOID-{existing.InvoiceNumber}");
                ledgerCmd.Parameters.AddWithValue("actor", (object?)validActorId ?? DBNull.Value);
                ledgerCmd.Parameters.AddWithValue("actorName", actorName);
                await ledgerCmd.ExecuteNonQueryAsync(ct);
            }

            var oldJson = JsonSerializer.Serialize(new { status = existing.InvoiceStatus, totalAmount = existing.TotalAmount });
            var newJson = JsonSerializer.Serialize(new { status = newStatus, refundAmount, reason, restoredUnits = targetLines.Sum(l => l.Quantity) });

            await using (var auditCmd = new NpgsqlCommand("""
                INSERT INTO public.audit_logs (user_id, action, entity_name, entity_id, old_values, new_values)
                VALUES (@actor, @action, 'showroom_invoices', @id, @old::jsonb, @new::jsonb);
                """, conn, tx))
            {
                auditCmd.Parameters.AddWithValue("actor", (object?)validActorId ?? DBNull.Value);
                auditCmd.Parameters.AddWithValue("action", isFullVoid ? "POS_INVOICE_VOIDED" : "POS_INVOICE_REFUNDED");
                auditCmd.Parameters.AddWithValue("id", invoiceId);
                auditCmd.Parameters.AddWithValue("old", oldJson);
                auditCmd.Parameters.AddWithValue("new", newJson);
                await auditCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return new(await GetInvoiceByIdAsync(invoiceId, ct));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<PosTillSessionRecord> GetTillSessionAsync(DateOnly date, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);
        return await ReadOrCreateTillSessionAsync(conn, date, ct);
    }

    public async Task<PosTillSessionRecord> SaveTillCountAsync(DateOnly date, TillDenominationRequest request, CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        var session = await ReadOrCreateTillSessionAsync(conn, date, ct);
        var counted = CounterReceiptService.ComputeCountedCash(
            request.Notes5000Count, request.Notes1000Count, request.Notes500Count, request.Notes100Count, request.Coins50Count);
        var variance = counted - session.ExpectedCashAmount;

        const string sql = """
            UPDATE public.pos_till_sessions
            SET notes_5000_count = @n5000,
                notes_1000_count = @n1000,
                notes_500_count = @n500,
                notes_100_count = @n100,
                coins_50_count = @c50,
                counted_cash_amount = @counted,
                expected_cash_amount = @expected,
                variance_amount = @variance,
                gross_day_revenue = @gross,
                safe_deposit_cash = @safeDeposit
            WHERE session_date = @date;
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("n5000", request.Notes5000Count);
            cmd.Parameters.AddWithValue("n1000", request.Notes1000Count);
            cmd.Parameters.AddWithValue("n500", request.Notes500Count);
            cmd.Parameters.AddWithValue("n100", request.Notes100Count);
            cmd.Parameters.AddWithValue("c50", request.Coins50Count);
            cmd.Parameters.AddWithValue("counted", counted);
            cmd.Parameters.AddWithValue("expected", session.ExpectedCashAmount);
            cmd.Parameters.AddWithValue("variance", variance);
            cmd.Parameters.AddWithValue("gross", session.GrossDayRevenue);
            cmd.Parameters.AddWithValue("safeDeposit", Math.Max(0m, counted - session.OpeningFloat));
            cmd.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return await ReadOrCreateTillSessionAsync(conn, date, ct);
    }

    public async Task<PosWriteResult<PosTillSessionRecord>> CloseTillDayAsync(
        int actorId,
        string actorName,
        DateOnly date,
        CloseTillDayRequest request,
        CancellationToken ct)
    {
        await using var conn = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(conn, ct);

        var session = await ReadOrCreateTillSessionAsync(conn, date, ct);
        if (string.Equals(session.SessionStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
            return new(Error: PosWriteError.TillClosed, Message: $"Register {session.TillNumber} is already closed for {date:yyyy-MM-dd}.");

        var counted = CounterReceiptService.ComputeCountedCash(
            request.Notes5000Count, request.Notes1000Count, request.Notes500Count, request.Notes100Count, request.Coins50Count);
        var variance = counted - session.ExpectedCashAmount;
        var safeDeposit = Math.Max(0m, counted - session.OpeningFloat);
        var zReportNo = $"ZR-{date:yyyy-MMdd}-01";
        var verifier = string.IsNullOrWhiteSpace(request.VerifiedByName) ? "Wimalasiri (Owner)" : request.VerifiedByName.Trim();

        const string sql = """
            UPDATE public.pos_till_sessions
            SET notes_5000_count = @n5000,
                notes_1000_count = @n1000,
                notes_500_count = @n500,
                notes_100_count = @n100,
                coins_50_count = @c50,
                counted_cash_amount = @counted,
                expected_cash_amount = @expected,
                variance_amount = @variance,
                gross_day_revenue = @gross,
                safe_deposit_cash = @safeDeposit,
                session_status = 'CLOSED',
                z_report_number = @zReport,
                closed_by_name = @closedBy,
                verified_by_name = @verifiedBy,
                closed_at = CURRENT_TIMESTAMP
            WHERE session_date = @date;
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("n5000", request.Notes5000Count);
            cmd.Parameters.AddWithValue("n1000", request.Notes1000Count);
            cmd.Parameters.AddWithValue("n500", request.Notes500Count);
            cmd.Parameters.AddWithValue("n100", request.Notes100Count);
            cmd.Parameters.AddWithValue("c50", request.Coins50Count);
            cmd.Parameters.AddWithValue("counted", counted);
            cmd.Parameters.AddWithValue("expected", session.ExpectedCashAmount);
            cmd.Parameters.AddWithValue("variance", variance);
            cmd.Parameters.AddWithValue("gross", session.GrossDayRevenue);
            cmd.Parameters.AddWithValue("safeDeposit", safeDeposit);
            cmd.Parameters.AddWithValue("zReport", zReportNo);
            cmd.Parameters.AddWithValue("closedBy", actorName);
            cmd.Parameters.AddWithValue("verifiedBy", verifier);
            cmd.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new(await ReadOrCreateTillSessionAsync(conn, date, ct));
    }

    private static async Task<PosTillSessionRecord> ReadOrCreateTillSessionAsync(NpgsqlConnection conn, DateOnly date, CancellationToken ct)
    {
        const string salesAggSql = """
            SELECT
                COUNT(*) FILTER (WHERE payment_mode = 'CASH' AND COALESCE(invoice_status, 'PAID') = 'PAID'),
                COALESCE(SUM(total_amount) FILTER (WHERE payment_mode = 'CASH' AND COALESCE(invoice_status, 'PAID') = 'PAID'), 0.00),
                COUNT(*) FILTER (WHERE payment_mode = 'CARD' AND COALESCE(invoice_status, 'PAID') = 'PAID'),
                COALESCE(SUM(total_amount) FILTER (WHERE payment_mode = 'CARD' AND COALESCE(invoice_status, 'PAID') = 'PAID'), 0.00),
                COUNT(*) FILTER (WHERE payment_mode = 'BANK_TRANSFER' AND COALESCE(invoice_status, 'PAID') IN ('PAID', 'PENDING')),
                COALESCE(SUM(total_amount) FILTER (WHERE payment_mode = 'BANK_TRANSFER' AND COALESCE(invoice_status, 'PAID') IN ('PAID', 'PENDING')), 0.00)
            FROM public.showroom_invoices
            WHERE DATE(invoice_date AT TIME ZONE 'Asia/Colombo') = @saleDate;
            """;

        int cashCount, cardCount, bankCount;
        decimal cashSales, cardSales, bankSales;
        await using (var aggCmd = new NpgsqlCommand(salesAggSql, conn))
        {
            aggCmd.Parameters.AddWithValue("saleDate", date.ToDateTime(TimeOnly.MinValue));
            await using var r = await aggCmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            cashCount = r.GetInt32(0);
            cashSales = r.GetDecimal(1);
            cardCount = r.GetInt32(2);
            cardSales = r.GetDecimal(3);
            bankCount = r.GetInt32(4);
            bankSales = r.GetDecimal(5);
        }

        const string ensureSessionSql = """
            INSERT INTO public.pos_till_sessions (
                session_date, till_number, opening_float, opened_at,
                notes_5000_count, notes_1000_count, notes_500_count, notes_100_count, coins_50_count,
                counted_cash_amount, expected_cash_amount, variance_amount
            ) VALUES (
                @date, 'Till #01', 25000.00, '08:30 AM',
                5, 0, 0, 0, 0,
                25000.00, 25000.00, 0.00
            )
            ON CONFLICT (session_date) DO NOTHING;

            SELECT
                session_id, till_number, opening_float, opened_at,
                notes_5000_count, notes_1000_count, notes_500_count, notes_100_count, coins_50_count,
                counted_cash_amount, session_status, z_report_number, closed_by_name, verified_by_name, closed_at
            FROM public.pos_till_sessions
            WHERE session_date = @date;
            """;

        await using var cmd = new NpgsqlCommand(ensureSessionSql, conn);
        cmd.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var sessionId = reader.GetInt32(0);
        var tillNo = reader.GetString(1);
        var openingFloat = reader.GetDecimal(2);
        var openedAt = reader.GetString(3);
        var n5000 = reader.GetInt32(4);
        var n1000 = reader.GetInt32(5);
        var n500 = reader.GetInt32(6);
        var n100 = reader.GetInt32(7);
        var c50 = reader.GetInt32(8);
        var counted = reader.GetDecimal(9);
        var status = reader.GetString(10);
        var zReport = reader.IsDBNull(11) ? null : reader.GetString(11);
        var closedBy = reader.IsDBNull(12) ? null : reader.GetString(12);
        var verifiedBy = reader.IsDBNull(13) ? null : reader.GetString(13);
        var closedAt = reader.IsDBNull(14) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(14);

        var expectedCash = openingFloat + cashSales;
        var grossRevenue = cashSales + cardSales + bankSales;
        var variance = counted - expectedCash;
        var safeDeposit = Math.Max(0m, counted - openingFloat);

        return new PosTillSessionRecord(
            sessionId, date, tillNo, openingFloat, openedAt,
            cashCount, cashSales, cardCount, cardSales, bankCount, bankSales,
            n5000, n1000, n500, n100, c50,
            counted, expectedCash, variance, grossRevenue, safeDeposit,
            status, zReport, closedBy, verifiedBy, closedAt);
    }

    private static async Task<Dictionary<int, List<ShowroomInvoiceLine>>> LoadLinesByInvoiceIdsAsync(
        NpgsqlConnection conn,
        int[] invoiceIds,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                sii.item_id,
                sii.invoice_id,
                sii.product_id,
                COALESCE(sii.sku_code_snapshot, ff.sku_code, 'SKU-' || sii.product_id::text) AS sku_code,
                COALESCE(sii.item_name_snapshot, ff.item_name, 'Showroom Furniture Item') AS item_name,
                sii.quantity,
                sii.unit_price,
                sii.subtotal,
                COALESCE(sii.is_refunded, FALSE) AS is_refunded
            FROM public.showroom_invoice_items sii
            LEFT JOIN public.finished_furniture ff ON ff.product_id = sii.product_id
            WHERE sii.invoice_id = ANY(@ids)
            ORDER BY sii.item_id ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", invoiceIds);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var map = new Dictionary<int, List<ShowroomInvoiceLine>>();
        while (await reader.ReadAsync(ct))
        {
            var invId = reader.GetInt32(1);
            if (!map.TryGetValue(invId, out var list))
            {
                list = [];
                map[invId] = list;
            }
            list.Add(new ShowroomInvoiceLine(
                reader.GetInt32(0),
                invId,
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetBoolean(8)));
        }
        return map;
    }

    private static async Task<int?> ResolveExistingUserIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int actorId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT user_id FROM public.users WHERE user_id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", actorId);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is int id ? id : null;
    }
}
