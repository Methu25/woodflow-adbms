namespace WoodFlow.Application.DTOs;

public sealed record OutstandingAdvanceLineDto(
    int AdvanceId,
    string VoucherReference,
    string DateLabel,
    string TitleLabel,
    string DisplayLine,
    decimal Amount,
    bool IsSettled
);

public sealed record EmployeeAdvanceProfileDto(
    int EmployeeId,
    string EmployeeCode,
    string FullName,
    string RoleTitle,
    string Department,
    string DropdownLabel,
    string Initials,
    decimal BaseMonthlySalary,
    decimal ApprovedPieceworkCredits,
    decimal TotalMtdGrossEarnings,
    IReadOnlyList<OutstandingAdvanceLineDto> OutstandingAdvances,
    decimal TotalOutstandingAdvances,
    decimal NetAvailableBeforeAdvance
);

public sealed record AdvanceIssuanceContextResponseDto(
    IReadOnlyList<EmployeeAdvanceProfileDto> Employees,
    IReadOnlyList<string> DisbursementSources,
    string SuggestedVoucherReference,
    decimal Till1AvailableBalance
);

public sealed record IssueEmployeeAdvanceRequest(
    int EmployeeId,
    decimal AdvanceAmount,
    string IssuanceDate,
    string DisbursementSource,
    string VoucherReference,
    string Reason,
    bool SupervisorOverrideApproved
);

public sealed record IssueEmployeeAdvanceResponseDto(
    int AdvanceId,
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string VoucherReference,
    decimal AdvanceAmount,
    string IssuanceDate,
    string DisbursementSource,
    string Reason,
    decimal NetAvailableBeforeAdvance,
    decimal BalanceAfterNewAdvance,
    bool IsDeficitFlagged,
    bool SupervisorOverrideApproved,
    string Message,
    EmployeeAdvanceProfileDto UpdatedEmployeeProfile
);
