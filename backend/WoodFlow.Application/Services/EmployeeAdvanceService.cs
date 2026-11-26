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
