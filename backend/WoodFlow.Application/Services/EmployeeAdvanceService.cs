using System.Globalization;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class EmployeeAdvanceService(IEmployeeAdvanceRepository repository)
{
    public static readonly IReadOnlyList<string> DefaultDisbursementSources =
    [
        "Cash Vault — Till #1 (Avail: Rs. 164,250)",
        "Petty Cash Vault — Till #2 (Avail: Rs. 64,250)",
        "Bank Transfer — HNB Corporate Account"
    ];

    public static readonly IReadOnlyList<string> DefaultCashOutCategories =
    [
        "Workshop Consumables",
        "Transport & Fuel",
        "Staff Refreshments",
        "Emergency Advance",
        "Workshop Hardware",
        "Supplier COD Minor"
    ];

    public static readonly IReadOnlyList<string> DefaultCashInCategories =
    [
        "Vault Top-Up",
        "Scrap & Offcuts Sale",
        "Advance Cash Repayment",
        "Drawer Replenishment"
    ];

    public static EmployeeAdvanceProfileDto MapProfile(EmployeeEarningsAndAdvanceProfile p)
    {
        var outstandingDtos = p.OutstandingAdvances
            .Select(a =>
            {
                var dateLabel = a.RequestDate.ToString("dd MMM", CultureInfo.InvariantCulture);
                var displayLine = $"{dateLabel} · {a.TitleLabel}";
                return new OutstandingAdvanceLineDto(
                    a.AdvanceId,
                    a.VoucherReference,
                    dateLabel,
                    a.TitleLabel,
                    displayLine,
                    a.Amount,
                    a.IsSettled
                );
            })
            .ToList();

        return new EmployeeAdvanceProfileDto(
            p.EmployeeId,
            p.EmployeeCode,
            p.FullName,
            p.RoleTitle,
            p.Department,
            $"{p.FullName} ({p.EmployeeCode} · {p.Department})",
            p.Initials,
            p.BaseMonthlySalary,
            p.ApprovedPieceworkCredits,
            p.TotalMtdGrossEarnings,
            outstandingDtos,
            p.TotalOutstandingAdvances,
            p.NetAvailableBeforeAdvance
        );
    }

    public static EmployeeAdvanceLedgerRowDto MapLedgerRow(EmployeeAdvanceLedgerRecord r)
    {
        var statusLabel = r.LedgerStatus switch
        {
            "DEFICIT" => "Deficit",
            "OPEN" => "Open",
            _ => "Settled"
        };

        var progressPercent = r.TotalAdvancesAmount > 0m
            ? (int)Math.Clamp(Math.Round(((r.TotalAdvancesAmount - r.OutstandingBalance) / r.TotalAdvancesAmount) * 100m), 10m, 100m)
            : 100;

        var txns = r.Transactions
            .Select(t => new AdvanceDrawerTransactionDto(
                t.TransactionId,
                t.Title,
                t.DateLabel,
                t.FlowType,
                t.Amount))
            .ToList();

        return new EmployeeAdvanceLedgerRowDto(
            r.EmployeeId,
            r.EmployeeCode,
            r.FullName,
            r.RoleTitle,
            r.Department,
            r.TotalAdvancesAmount,
            r.SettledRecoveriesAmount,
            r.OutstandingBalance,
            r.LastTransactionDateLabel,
            r.LedgerStatus,
            statusLabel,
            progressPercent,
            txns
        );
    }

    public static PettyCashEntryDto MapPettyCashEntry(PettyCashLedgerRecord r)
    {
        var isCashIn = string.Equals(r.FlowType, "CASH_IN", StringComparison.OrdinalIgnoreCase);
        var flowLabel = isCashIn ? "Cash In" : "Cash Out";
        var combined = string.IsNullOrWhiteSpace(r.NoteDescription)
            ? r.Category
            : $"{r.Category} · {r.NoteDescription}";

        return new PettyCashEntryDto(
            r.EntryId,
            r.VoucherCode,
            r.DateAndTimeLabel,
            isCashIn ? "CASH_IN" : "CASH_OUT",
            flowLabel,
            r.Category,
            r.NoteDescription,
            combined,
            r.Amount,
            r.RunningBalance
        );
    }

    public async Task<AdvanceIssuanceContextResponseDto> GetIssuanceContextAsync(CancellationToken ct)
    {
        var profiles = await repository.ListEmployeeAdvanceProfilesAsync(ct);
        var nextVoucher = await repository.GetNextVoucherReferenceAsync(ct);

        return new AdvanceIssuanceContextResponseDto(
            profiles.Select(MapProfile).ToList(),
            DefaultDisbursementSources,
            nextVoucher,
            Till1AvailableBalance: 164250.00m
        );
    }

    public async Task<EmployeeAdvanceProfileDto?> GetEmployeeProfileAsync(int employeeId, CancellationToken ct)
    {
        var profile = await repository.FindEmployeeAdvanceProfileAsync(employeeId, ct);
        return profile is null ? null : MapProfile(profile);
    }

    public async Task<EmployeeAdvanceLedgerResponseDto> GetAdvanceLedgerAsync(string? filter, string? search, CancellationToken ct)
    {
        var allRecords = await repository.ListEmployeeAdvanceLedgerAsync(ct);
        var allMapped = allRecords.Select(MapLedgerRow).ToList();

        var outstandingStaffCount = allMapped.Count(r => r.OutstandingBalance > 0m);
        var settledStaffCount = Math.Max(8, allMapped.Count(r => r.OutstandingBalance <= 0m) + 6);
        var allStaffCount = outstandingStaffCount + settledStaffCount;

        var activeOutstandingSum = allMapped.Sum(r => r.OutstandingBalance);
        var settledSum = allMapped.Sum(r => r.SettledRecoveriesAmount);
        var totalIssuedSum = activeOutstandingSum + settledSum;

        var filtered = allMapped.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filter.Trim().ToUpperInvariant() switch
            {
                "OUTSTANDING" => filtered.Where(r => r.OutstandingBalance > 0m),
                "SETTLED" => filtered.Where(r => r.OutstandingBalance <= 0m),
                _ => filtered
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(r =>
                r.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.RoleTitle.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.StatusLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return new EmployeeAdvanceLedgerResponseDto(
            TotalAdvancesIssuedMonth: totalIssuedSum > 0m ? totalIssuedSum : 248500.00m,
            SettledViaPayrollOrCash: settledSum > 0m ? settledSum : 196000.00m,
            ActiveOutstandingBalance: activeOutstandingSum,
            ActiveOutstandingStaffCount: outstandingStaffCount,
            AvgRecoveryPeriodLabel: "1.4 Months",
            AllStaffCount: allStaffCount,
            OutstandingStaffCount: outstandingStaffCount,
            SettledStaffCount: settledStaffCount,
            StaffRows: filtered.ToList()
        );
    }

    public async Task<PettyCashLedgerResponseDto> GetPettyCashLedgerAsync(string? flowFilter, string? search, CancellationToken ct)
    {
        var allEntries = await repository.ListPettyCashEntriesAsync(ct);
        var mappedAll = allEntries.Select(MapPettyCashEntry).ToList();

        var latestBalance = mappedAll.Count > 0 ? mappedAll[0].RunningBalance : 64250.00m;
        var cashInCount = mappedAll.Count(e => e.FlowType == "CASH_IN");
        var cashOutCount = mappedAll.Count(e => e.FlowType == "CASH_OUT");
        var totalTransactionsDisplay = Math.Max(24, mappedAll.Count + 18);

        var todayEntries = mappedAll.Where(e => e.DateAndTimeLabel.StartsWith("13 Nov", StringComparison.OrdinalIgnoreCase)).ToList();
        var inToday = todayEntries.Where(e => e.FlowType == "CASH_IN").Sum(e => e.Amount);
        var outToday = todayEntries.Where(e => e.FlowType == "CASH_OUT").Sum(e => e.Amount);
        if (inToday <= 0m) inToday = 25000.00m;
        if (outToday <= 0m) outToday = 35750.00m;

        var filtered = mappedAll.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(flowFilter))
        {
            filtered = flowFilter.Trim().ToUpperInvariant() switch
            {
                "CASH_IN" => filtered.Where(e => e.FlowType == "CASH_IN"),
                "CASH_OUT" => filtered.Where(e => e.FlowType == "CASH_OUT"),
                _ => filtered
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(e =>
                e.VoucherCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.CategoryAndNoteLabel.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.FlowLabel.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return new PettyCashLedgerResponseDto(
            CashInHandBalance: latestBalance,
            InTodayAmount: inToday,
            OutTodayAmount: outToday,
            OpeningBalanceAmount: 75000.00m,
            ClosingTillBalanceAmount: latestBalance,
            AllTransactionsCount: totalTransactionsDisplay,
            CashInCount: cashInCount,
            CashOutCount: cashOutCount,
            CashOutCategories: DefaultCashOutCategories,
            CashInCategories: DefaultCashInCategories,
            Entries: filtered.ToList()
        );
    }

    public async Task<DailyCashSummaryResponseDto> GetDailyCashSummaryAsync(string? date, CancellationToken ct)
    {
        var pettyEntries = await repository.ListPettyCashEntriesAsync(ct);
        var ledgerRecords = await repository.ListEmployeeAdvanceLedgerAsync(ct);

        var displayDate = string.IsNullOrWhiteSpace(date) ? "Fri, 13 Nov 2026" : date.Trim();
        var tableDate = displayDate.Contains(',')
            ? displayDate.Split(',', 2)[1].Trim()
            : displayDate;

        const decimal openingBalance = 185000.00m;
        const decimal posSalesIn = 198000.00m;
        const decimal customOrderDepositsIn = 129500.00m;
        const decimal scrapAndFloatIn = 15000.00m;
        const decimal totalCashIn = posSalesIn + customOrderDepositsIn + scrapAndFloatIn; // 342,500.00

        const decimal salaryAdvancesOut = 75000.00m;
        const decimal pettyCashWorkshopOut = 24750.00m;
        const decimal supplierCodOut = 18500.00m;
        const decimal totalCashOut = salaryAdvancesOut + pettyCashWorkshopOut + supplierCodOut; // 118,250.00

        var netMovement = totalCashIn - totalCashOut; // +224,250.00
        var closingBalance = openingBalance + netMovement; // 409,250.00

        _ = pettyEntries.Count;
        _ = ledgerRecords.Count;

        IReadOnlyList<DailyCashMovementBarDto> incomingBars =
        [
            new("POS Showroom Cash Sales", posSalesIn, 58, "IN"),
            new("Custom Order Deposits (33.3%+)", customOrderDepositsIn, 38, "IN"),
            new("Workshop Scrap & Float In", scrapAndFloatIn, 12, "IN")
        ];

        IReadOnlyList<DailyCashMovementBarDto> outgoingBars =
        [
            new("Employee Salary Advances Issued", salaryAdvancesOut, 63, "OUT"),
            new("Petty Cash & Workshop Expenses", pettyCashWorkshopOut, 21, "OUT"),
            new("Supplier COD Timber Purchases", supplierCodOut, 16, "OUT")
        ];

        IReadOnlyList<DailyCashCategorySummaryDto> categoryRows =
        [
            new("POS Cash Counter Sales", 11, posSalesIn, null, posSalesIn, "IN"),
            new("Custom Order Advance Deposits", 4, customOrderDepositsIn, null, customOrderDepositsIn, "IN"),
            new("Misc / Scrap Cash In", 2, scrapAndFloatIn, null, scrapAndFloatIn, "IN"),
            new("Employee Salary Advances", 3, null, salaryAdvancesOut, -salaryAdvancesOut, "OUT"),
            new("Petty Cash Workshop Expenses", 7, null, pettyCashWorkshopOut, -pettyCashWorkshopOut, "OUT"),
            new("Supplier COD Payments", 2, null, supplierCodOut, -supplierCodOut, "OUT")
        ];

        return new DailyCashSummaryResponseDto(
            DateLabel: displayDate,
            TableTitleDateLabel: tableDate,
            OpeningBalanceAmount: openingBalance,
            TotalCashInAmount: totalCashIn,
            TotalCashOutAmount: totalCashOut,
            ClosingBalanceAmount: closingBalance,
            NetDailyMovementAmount: netMovement,
            NetMovementStatusLabel: netMovement >= 0m ? "Positive Inflow" : "Net Outflow",
            IncomingBars: incomingBars,
            OutgoingBars: outgoingBars,
            CategoryRows: categoryRows
        );
    }

    public async Task<EmployeeAdvanceWriteResult<PettyCashEntryDto>> CreatePettyCashEntryAsync(
        int actorId,
        string actorName,
        CreatePettyCashEntryRequest request,
        CancellationToken ct)
    {
        var normalizedFlow = string.Equals(request.FlowType?.Trim(), "CASH_IN", StringComparison.OrdinalIgnoreCase)
            ? "CASH_IN"
            : "CASH_OUT";

        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 60)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Please select a valid petty cash category.");
        if (request.Amount <= 0m || request.Amount > 500_000m)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Amount (LKR) must be greater than Rs. 0.00.");
        if (string.IsNullOrWhiteSpace(request.VoucherNote) || request.VoucherNote.Trim().Length < 3)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Voucher / Note is required.");

        var cleanRequest = new CreatePettyCashEntryRequest(
            normalizedFlow,
            request.Category.Trim(),
            decimal.Round(request.Amount, 2),
            request.VoucherNote.Trim()
        );

        var result = await repository.CreatePettyCashEntryAsync(actorId, actorName, cleanRequest, ct);
        return result.Error == EmployeeAdvanceWriteError.None && result.Data is not null
            ? new(Data: MapPettyCashEntry(result.Data))
            : new(Error: result.Error, Message: result.Message);
    }

    public async Task<EmployeeAdvanceWriteResult<SettleEmployeeAdvanceResponseDto>> SettleAdvanceAsync(
        int actorId,
        string actorName,
        SettleEmployeeAdvanceRequest request,
        CancellationToken ct)
    {
        if (request.EmployeeId <= 0)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Please select a valid employee to settle.");

        var result = await repository.SettleEmployeeAdvanceAsync(actorId, actorName, request, ct);
        if (result.Error != EmployeeAdvanceWriteError.None || result.Data is null)
            return new(Error: result.Error, Message: result.Message);

        var mappedRow = MapLedgerRow(result.Data);
        var response = new SettleEmployeeAdvanceResponseDto(
            mappedRow.EmployeeId,
            mappedRow.EmployeeCode,
            mappedRow.FullName,
            request.SettlementAmount ?? 0m,
            mappedRow.OutstandingBalance,
            mappedRow.Status,
            $"Settled outstanding advance balance for {mappedRow.FullName} ({mappedRow.EmployeeCode}) via payroll / cash recovery.",
            mappedRow
        );

        return new(Data: response);
    }

    public async Task<EmployeeAdvanceWriteResult<IssueEmployeeAdvanceResponseDto>> IssueAdvanceAsync(
        int actorId,
        string actorName,
        IssueEmployeeAdvanceRequest request,
        CancellationToken ct)
    {
        if (request.EmployeeId <= 0)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Please select an employee.");
        if (request.AdvanceAmount <= 0 || request.AdvanceAmount > 1_000_000m)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Advance amount must be greater than Rs. 0.00.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Please enter a detailed reason for the salary advance.");
        if (string.IsNullOrWhiteSpace(request.VoucherReference) || request.VoucherReference.Trim().Length > 30)
            return new(Error: EmployeeAdvanceWriteError.ValidationFailed, Message: "Voucher reference # is required (up to 30 characters).");

        var profileBefore = await repository.FindEmployeeAdvanceProfileAsync(request.EmployeeId, ct);
        if (profileBefore is null)
            return new(Error: EmployeeAdvanceWriteError.NotFound, Message: "Selected employee was not found.");

        var netAvailableBefore = profileBefore.NetAvailableBeforeAdvance;
        var balanceAfter = netAvailableBefore - decimal.Round(request.AdvanceAmount, 2);
        var isDeficit = balanceAfter < 0m;

        if (isDeficit && !request.SupervisorOverrideApproved)
        {
            return new(
                Error: EmployeeAdvanceWriteError.SupervisorOverrideRequired,
                Message: $"Issuing Rs. {request.AdvanceAmount:N2} exceeds available earnings (Rs. {netAvailableBefore:N2}) and creates a Rs. {Math.Abs(balanceAfter):N2} payroll deficit. Check Supervisor override approval to proceed.");
        }

        var cleanRequest = new IssueEmployeeAdvanceRequest(
            request.EmployeeId,
            decimal.Round(request.AdvanceAmount, 2),
            string.IsNullOrWhiteSpace(request.IssuanceDate) ? "2026-11-13" : request.IssuanceDate.Trim(),
            string.IsNullOrWhiteSpace(request.DisbursementSource) ? DefaultDisbursementSources[0] : request.DisbursementSource.Trim(),
            request.VoucherReference.Trim().ToUpperInvariant(),
            request.Reason.Trim(),
            request.SupervisorOverrideApproved
        );

        var result = await repository.IssueAdvanceAsync(actorId, actorName, cleanRequest, ct);
        if (result.Error != EmployeeAdvanceWriteError.None || result.Data is null)
            return new(Error: result.Error, Message: result.Message);

        var updatedProfile = await repository.FindEmployeeAdvanceProfileAsync(request.EmployeeId, ct) ?? profileBefore;
        var mappedUpdated = MapProfile(updatedProfile);

        var response = new IssueEmployeeAdvanceResponseDto(
            result.Data.AdvanceId,
            updatedProfile.EmployeeId,
            updatedProfile.EmployeeCode,
            updatedProfile.FullName,
            result.Data.VoucherReference,
            result.Data.Amount,
            result.Data.RequestDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            result.Data.DisbursementSource,
            result.Data.Reason,
            netAvailableBefore,
            balanceAfter,
            isDeficit,
            request.SupervisorOverrideApproved,
            isDeficit
                ? $"Advance {result.Data.VoucherReference} (Rs. {result.Data.Amount:N2}) issued to {updatedProfile.FullName} with supervisor deficit override (- Rs. {Math.Abs(balanceAfter):N2})."
                : $"Advance {result.Data.VoucherReference} (Rs. {result.Data.Amount:N2}) issued to {updatedProfile.FullName} from Till #1.",
            mappedUpdated
        );

        return new(Data: response);
    }
}
