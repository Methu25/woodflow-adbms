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
