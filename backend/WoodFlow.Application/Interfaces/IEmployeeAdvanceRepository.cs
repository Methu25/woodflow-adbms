using WoodFlow.Application.DTOs;
using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public enum EmployeeAdvanceWriteError
{
    None,
    NotFound,
    DuplicateVoucher,
    SupervisorOverrideRequired,
    ValidationFailed
}

public sealed record EmployeeAdvanceWriteResult<T>(
    T? Data = default,
    EmployeeAdvanceWriteError Error = EmployeeAdvanceWriteError.None,
    string? Message = null
);

public interface IEmployeeAdvanceRepository
{
    Task<IReadOnlyList<EmployeeEarningsAndAdvanceProfile>> ListEmployeeAdvanceProfilesAsync(CancellationToken ct);
    Task<EmployeeEarningsAndAdvanceProfile?> FindEmployeeAdvanceProfileAsync(int employeeId, CancellationToken ct);
    Task<string> GetNextVoucherReferenceAsync(CancellationToken ct);
    Task<EmployeeAdvanceWriteResult<EmployeeAdvanceItem>> IssueAdvanceAsync(
        int actorId,
        string actorName,
        IssueEmployeeAdvanceRequest request,
        CancellationToken ct);

    Task<IReadOnlyList<EmployeeAdvanceLedgerRecord>> ListEmployeeAdvanceLedgerAsync(CancellationToken ct);
    Task<EmployeeAdvanceWriteResult<EmployeeAdvanceLedgerRecord>> SettleEmployeeAdvanceAsync(
        int actorId,
        string actorName,
        SettleEmployeeAdvanceRequest request,
        CancellationToken ct);
}
