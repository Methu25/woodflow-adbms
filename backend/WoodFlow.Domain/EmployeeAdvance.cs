namespace WoodFlow.Domain;

public sealed record EmployeeAdvanceItem(
    int AdvanceId,
    int EmployeeId,
    string VoucherReference,
    string TitleLabel,
    decimal Amount,
    DateOnly RequestDate,
    string DisbursementSource,
    string Reason,
    bool SupervisorOverrideApproved,
    bool IsSettled,
    int? IssuedByUserId
);

public sealed record EmployeeEarningsAndAdvanceProfile(
    int EmployeeId,
    string EmployeeCode,
    string FullName,
    string RoleTitle,
    string Department,
    string Initials,
    decimal BaseMonthlySalary,
    decimal ApprovedPieceworkCredits,
    decimal TotalMtdGrossEarnings,
    IReadOnlyList<EmployeeAdvanceItem> OutstandingAdvances,
    decimal TotalOutstandingAdvances,
    decimal NetAvailableBeforeAdvance
);
