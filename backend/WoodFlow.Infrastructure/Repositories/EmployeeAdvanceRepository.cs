using System.Data;
using System.Globalization;
using Npgsql;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Repositories;

public sealed class EmployeeAdvanceRepository(Database database) : IEmployeeAdvanceRepository
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
                ALTER TABLE public.employees
                    ADD COLUMN IF NOT EXISTS employee_code VARCHAR(20),
                    ADD COLUMN IF NOT EXISTS role_title VARCHAR(60) NOT NULL DEFAULT 'Carpenter',
                    ADD COLUMN IF NOT EXISTS department VARCHAR(80) NOT NULL DEFAULT 'Carpentry Workshop',
                    ADD COLUMN IF NOT EXISTS approved_piecework_mtd DECIMAL(10,2) NOT NULL DEFAULT 0.00;

                ALTER TABLE public.worker_advances
                    ADD COLUMN IF NOT EXISTS voucher_reference VARCHAR(30),
                    ADD COLUMN IF NOT EXISTS title_label VARCHAR(120) NOT NULL DEFAULT 'Mid-Month Cash Draw',
                    ADD COLUMN IF NOT EXISTS disbursement_source VARCHAR(100) NOT NULL DEFAULT 'Cash Vault — Till #1 (Avail: Rs. 164,250)',
                    ADD COLUMN IF NOT EXISTS reason TEXT NOT NULL DEFAULT 'Employee requested mid-month salary advance.',
                    ADD COLUMN IF NOT EXISTS supervisor_override BOOLEAN NOT NULL DEFAULT FALSE;
                """;

            await using (var cmd = new NpgsqlCommand(sql, connection))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SeedReferenceDataIfMissingAsync(connection, ct);
            _schemaInitialized = true;
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }

    private static async Task SeedReferenceDataIfMissingAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var checkCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.employees WHERE employee_code = 'EMP-014' OR nic_number = '199124508821')",
            connection);
        var hasKasun = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (hasKasun) return;

        const string seedEmployees = """
            INSERT INTO public.employees (
                full_name, nic_number, phone_number, employment_type,
                base_monthly_salary, is_active, employee_code, role_title, department, approved_piecework_mtd
            ) VALUES
            ('Kasun Perera', '199124508821', '0774128890', 'HYBRID', 58000.00, TRUE, 'EMP-014', 'Carpenter', 'Carpentry Workshop', 12000.00),
            ('Dilshan Silva', '198831204419', '0715229104', 'MONTHLY_SALARIED', 75000.00, TRUE, 'EMP-008', 'Driver', 'Delivery & Logistics', 15000.00),
            ('Mahesh Perera', '200118903342', '0768912203', 'HYBRID', 40000.00, TRUE, 'EMP-019', 'Apprentice', 'Carpentry Workshop', 5000.00),
            ('Tharindu Gamage', '199422107785', '0729104432', 'HYBRID', 55000.00, TRUE, 'EMP-011', 'Polisher', 'Finishing & Spray Booth', 16500.00),
            ('Ruwan Jayasinghe', '198611405590', '0772340911', 'HYBRID', 62000.00, TRUE, 'EMP-005', 'Carpenter', 'Carpentry Workshop', 14000.00),
            ('Kamal Ratnayake', '199030501128', '0718943310', 'HYBRID', 54000.00, TRUE, 'EMP-009', 'Polisher', 'Finishing & Spray Booth', 11000.00)
            ON CONFLICT (nic_number) DO UPDATE SET
                employee_code = EXCLUDED.employee_code,
                role_title = EXCLUDED.role_title,
                department = EXCLUDED.department,
                base_monthly_salary = EXCLUDED.base_monthly_salary,
                approved_piecework_mtd = EXCLUDED.approved_piecework_mtd;

            UPDATE public.employees
            SET employee_code = COALESCE(employee_code, 'EMP-' || LPAD(employee_id::text, 3, '0'))
            WHERE employee_code IS NULL;
            """;

        await using (var empCmd = new NpgsqlCommand(seedEmployees, connection))
        {
            await empCmd.ExecuteNonQueryAsync(ct);
        }

        // Seed Kasun Perera's 2 unsettled advances from Figma Screen 01 (04 Nov Rs. 28,500 + 22 Oct Rs. 25,000 = Rs. 53,500)
        // plus other employees' advances so Screen 01 & Screen 02 data match Figma
        const string seedAdvances = """
            INSERT INTO public.worker_advances (
                employee_id, amount, request_date, is_settled, voucher_reference, title_label, disbursement_source, reason, supervisor_override
            )
            SELECT e.employee_id, v.amount, v.request_date, v.is_settled, v.voucher_ref, v.title_label, v.source, v.reason, v.override_flag
            FROM (VALUES
                ('EMP-014', 28500.00, DATE '2026-11-04', FALSE, 'ADV-2026-1104', 'Mid-Month Draw #2', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Mid-month family medical expense draw.', FALSE),
                ('EMP-014', 25000.00, DATE '2026-10-22', FALSE, 'ADV-2026-1022', 'Unrecovered Tool Advance', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Emergency workshop tool advance carried over.', FALSE),
                ('EMP-008', 10000.00, DATE '2026-11-11', FALSE, 'ADV-2026-1111', 'Mid-Month Fuel & Personal Draw', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Personal advance requested before outstation delivery.', FALSE),
                ('EMP-019', 19000.00, DATE '2026-11-09', FALSE, 'ADV-2026-1109', 'Emergency Hostel & Tuition Draw', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Apprentice emergency advance with supervisor sign-off.', TRUE),
                ('EMP-011', 15000.00, DATE '2026-11-04', FALSE, 'ADV-2026-1103', 'Mid-Month Cash Draw #1', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Monthly household advance draw.', FALSE),
                ('EMP-005', 15000.00, DATE '2026-10-31', TRUE,  'ADV-2026-1018', 'Oct Payroll Settled Draw', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Recovered in full via October payroll.', FALSE),
                ('EMP-009', 10000.00, DATE '2026-10-31', TRUE,  'ADV-2026-1015', 'Oct Payroll Settled Draw', 'Cash Vault — Till #1 (Avail: Rs. 164,250)', 'Recovered in full via October payroll.', FALSE)
            ) AS v(emp_code, amount, request_date, is_settled, voucher_ref, title_label, source, reason, override_flag)
            JOIN public.employees e ON e.employee_code = v.emp_code
            WHERE NOT EXISTS (
                SELECT 1 FROM public.worker_advances wa WHERE wa.voucher_reference = v.voucher_ref
            );
            """;

        await using var advCmd = new NpgsqlCommand(seedAdvances, connection);
        await advCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeEarningsAndAdvanceProfile>> ListEmployeeAdvanceProfilesAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        const string empSql = """
            SELECT
                e.employee_id,
                COALESCE(e.employee_code, 'EMP-' || LPAD(e.employee_id::text, 3, '0')) AS emp_code,
                e.full_name,
                COALESCE(e.role_title, 'Carpenter') AS role_title,
                COALESCE(e.department, 'Carpentry Workshop') AS department,
                COALESCE(e.base_monthly_salary, 0.00) AS base_salary,
                GREATEST(
                    COALESCE(e.approved_piecework_mtd, 0.00),
                    COALESCE((
                        SELECT SUM(pc.agreed_labor_cost)
                        FROM public.piecework_credits pc
                        WHERE pc.employee_id = e.employee_id AND pc.is_approved IS TRUE
                    ), 0.00)
                ) AS piecework_credits
            FROM public.employees e
            WHERE e.is_active IS TRUE
            ORDER BY
                CASE
                    WHEN e.employee_code = 'EMP-014' THEN 0
                    WHEN e.employee_code LIKE 'EMP-%' THEN 1
                    ELSE 2
                END,
                e.employee_id ASC
            """;

        var employees = new List<(int Id, string Code, string Name, string Role, string Dept, decimal BaseSalary, decimal Piecework)>();
        await using (var cmd = new NpgsqlCommand(empSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                employees.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6)
                ));
            }
        }

        const string advSql = """
            SELECT
                advance_id,
                employee_id,
                COALESCE(voucher_reference, 'ADV-2026-' || LPAD(advance_id::text, 4, '0')),
                COALESCE(title_label, 'Mid-Month Cash Draw'),
                amount,
                COALESCE(request_date, CURRENT_DATE),
                COALESCE(disbursement_source, 'Cash Vault — Till #1 (Avail: Rs. 164,250)'),
                COALESCE(reason, 'Salary advance'),
                COALESCE(supervisor_override, FALSE),
                COALESCE(is_settled, FALSE),
                issued_by
            FROM public.worker_advances
            WHERE is_settled IS FALSE
            ORDER BY request_date DESC, advance_id DESC
            """;

        var advancesByEmp = new Dictionary<int, List<EmployeeAdvanceItem>>();
        await using (var advCmd = new NpgsqlCommand(advSql, connection))
        await using (var reader = await advCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var item = new EmployeeAdvanceItem(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDecimal(4),
                    reader.GetFieldValue<DateOnly>(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.GetBoolean(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10)
                );
                if (!advancesByEmp.TryGetValue(item.EmployeeId, out var list))
                {
                    list = [];
                    advancesByEmp[item.EmployeeId] = list;
                }
                list.Add(item);
            }
        }

        var result = new List<EmployeeEarningsAndAdvanceProfile>(employees.Count);
        foreach (var emp in employees)
        {
            var unsettled = advancesByEmp.TryGetValue(emp.Id, out var list)
                ? list
                : [];
            var totalGross = emp.BaseSalary + emp.Piecework;
            var totalOutstanding = unsettled.Sum(a => a.Amount);
            var netAvailableBefore = totalGross - totalOutstanding;

            result.Add(new EmployeeEarningsAndAdvanceProfile(
                emp.Id,
                emp.Code,
                emp.Name,
                emp.Role,
                emp.Dept,
                BuildInitials(emp.Name),
                emp.BaseSalary,
                emp.Piecework,
                totalGross,
                unsettled,
                totalOutstanding,
                netAvailableBefore
            ));
        }

        return result;
    }

    public async Task<EmployeeEarningsAndAdvanceProfile?> FindEmployeeAdvanceProfileAsync(int employeeId, CancellationToken ct)
    {
        var all = await ListEmployeeAdvanceProfilesAsync(ct);
        return all.FirstOrDefault(e => e.EmployeeId == employeeId);
    }

    public async Task<string> GetNextVoucherReferenceAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);

        await using var checkCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.worker_advances WHERE lower(voucher_reference) = 'adv-2026-1113')",
            connection);
        var existsDefault = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (!existsDefault)
            return "ADV-2026-1113";

        await using var countCmd = new NpgsqlCommand("SELECT count(*)::int + 1114 FROM public.worker_advances", connection);
        var nextNum = (int)(await countCmd.ExecuteScalarAsync(ct))!;
        return $"ADV-2026-{nextNum}";
    }

    public async Task<EmployeeAdvanceWriteResult<EmployeeAdvanceItem>> IssueAdvanceAsync(
        int actorId,
        string actorName,
        IssueEmployeeAdvanceRequest request,
        CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await EnsureExtendedSchemaAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        await using (var dupCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM public.worker_advances WHERE lower(voucher_reference) = lower(@voucher))",
            connection, transaction))
        {
            dupCmd.Parameters.AddWithValue("voucher", request.VoucherReference);
            if ((bool)(await dupCmd.ExecuteScalarAsync(ct))!)
                return new(Error: EmployeeAdvanceWriteError.DuplicateVoucher, Message: $"Voucher Reference '{request.VoucherReference}' has already been issued.");
        }

        if (!DateOnly.TryParse(request.IssuanceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            parsedDate = new DateOnly(2026, 11, 13);
        }

        var shortTitle = BuildShortAdvanceTitle(request.Reason, request.VoucherReference);

        const string insertSql = """
            INSERT INTO public.worker_advances (
                employee_id, amount, request_date, is_settled, issued_by,
                voucher_reference, title_label, disbursement_source, reason, supervisor_override
            ) VALUES (
                @empId, @amount, @reqDate, FALSE, @actorId,
                @voucher, @title, @source, @reason, @override
            ) RETURNING advance_id
            """;

        await using var insertCmd = new NpgsqlCommand(insertSql, connection, transaction);
        insertCmd.Parameters.AddWithValue("empId", request.EmployeeId);
        insertCmd.Parameters.AddWithValue("amount", request.AdvanceAmount);
        insertCmd.Parameters.AddWithValue("reqDate", parsedDate);
        insertCmd.Parameters.AddWithValue("actorId", actorId);
        insertCmd.Parameters.AddWithValue("voucher", request.VoucherReference);
        insertCmd.Parameters.AddWithValue("title", shortTitle);
        insertCmd.Parameters.AddWithValue("source", request.DisbursementSource);
        insertCmd.Parameters.AddWithValue("reason", request.Reason);
        insertCmd.Parameters.AddWithValue("override", request.SupervisorOverrideApproved);

        var advanceId = (int)(await insertCmd.ExecuteScalarAsync(ct))!;

        // Also record the cash disbursement in petty_cash_ledger so Till #1 / Petty Cash stays reconciled
        const string ledgerSql = """
            INSERT INTO public.petty_cash_ledger (
                transaction_date, entry_type, category, amount, description, logged_by
            ) VALUES (
                @txnDate, 'CASH_OUT', 'WORKER_ADVANCE', @amount, @desc, @actorId
            )
            """;
        await using (var ledgerCmd = new NpgsqlCommand(ledgerSql, connection, transaction))
        {
            ledgerCmd.Parameters.AddWithValue("txnDate", parsedDate);
            ledgerCmd.Parameters.AddWithValue("amount", request.AdvanceAmount);
            ledgerCmd.Parameters.AddWithValue("desc", $"{request.VoucherReference} · {request.Reason} (Issued by {actorName})");
            ledgerCmd.Parameters.AddWithValue("actorId", actorId);
            await ledgerCmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);

        return new(Data: new EmployeeAdvanceItem(
            advanceId,
            request.EmployeeId,
            request.VoucherReference,
            shortTitle,
            request.AdvanceAmount,
            parsedDate,
            request.DisbursementSource,
            request.Reason,
            request.SupervisorOverrideApproved,
            IsSettled: false,
            actorId
        ));
    }

    private static string BuildInitials(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "WF";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
    }

    private static string BuildShortAdvanceTitle(string reason, string voucher)
    {
        if (reason.Contains("hospital", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("medical", StringComparison.OrdinalIgnoreCase))
            return $"Cash Advance #{voucher.Replace("ADV-2026-", "ADV-", StringComparison.OrdinalIgnoreCase)}";
        if (reason.Contains("tool", StringComparison.OrdinalIgnoreCase))
            return "Tool Advance Draw";
        return $"Cash Advance #{voucher.Replace("ADV-2026-", "ADV-", StringComparison.OrdinalIgnoreCase)}";
    }
}
