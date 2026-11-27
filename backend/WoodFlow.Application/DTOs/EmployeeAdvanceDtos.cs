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

public sealed record AdvanceDrawerTransactionDto(
    string TransactionId,
    string Title,
    string DateLabel,
    string FlowType, // "IN" | "OUT"
    decimal Amount
);

public sealed record EmployeeAdvanceLedgerRowDto(
    int EmployeeId,
    string EmployeeCode,
    string FullName,
    string RoleTitle,
    string Department,
    decimal TotalAdvancesAmount,
    decimal SettledRecoveriesAmount,
    decimal OutstandingBalance,
    string LastTransactionDateLabel,
    string Status, // "DEFICIT" | "OPEN" | "SETTLED"
    string StatusLabel, // "Deficit" | "Open" | "Settled"
    int RecoveryProgressPercent,
    IReadOnlyList<AdvanceDrawerTransactionDto> Transactions
);

public sealed record EmployeeAdvanceLedgerResponseDto(
    decimal TotalAdvancesIssuedMonth,
    decimal SettledViaPayrollOrCash,
    decimal ActiveOutstandingBalance,
    int ActiveOutstandingStaffCount,
    string AvgRecoveryPeriodLabel,
    int AllStaffCount,
    int OutstandingStaffCount,
    int SettledStaffCount,
    IReadOnlyList<EmployeeAdvanceLedgerRowDto> StaffRows
);

public sealed record SettleEmployeeAdvanceRequest(
    int EmployeeId,
    decimal? SettlementAmount = null,
    string? SettlementMethod = "PAYROLL_DEDUCT",
    string? SettlementPeriod = "2026-11"
);

public sealed record SettleEmployeeAdvanceResponseDto(
    int EmployeeId,
    string EmployeeCode,
    string FullName,
    decimal SettledAmount,
    decimal RemainingOutstandingBalance,
    string Status,
    string Message,
    EmployeeAdvanceLedgerRowDto UpdatedLedgerRow
);

public sealed record PettyCashEntryDto(
    int EntryId,
    string VoucherCode,
    string DateAndTimeLabel,
    string FlowType, // "CASH_IN" | "CASH_OUT"
    string FlowLabel, // "Cash In" | "Cash Out"
    string Category,
    string NoteDescription,
    string CategoryAndNoteLabel,
    decimal Amount,
    decimal RunningBalance
);

public sealed record PettyCashLedgerResponseDto(
    decimal CashInHandBalance,
    decimal InTodayAmount,
    decimal OutTodayAmount,
    decimal OpeningBalanceAmount,
    decimal ClosingTillBalanceAmount,
    int AllTransactionsCount,
    int CashInCount,
    int CashOutCount,
    IReadOnlyList<string> CashOutCategories,
    IReadOnlyList<string> CashInCategories,
    IReadOnlyList<PettyCashEntryDto> Entries
);

public sealed record CreatePettyCashEntryRequest(
    string FlowType, // "CASH_IN" | "CASH_OUT"
    string Category,
    decimal Amount,
    string VoucherNote
);

public sealed record DailyCashMovementBarDto(
    string Label,
    decimal Amount,
    int WidthPercent,
    string FlowType // "IN" | "OUT"
);

public sealed record DailyCashCategorySummaryDto(
    string CategoryName,
    int TransactionCount,
    decimal? CashInAmount,
    decimal? CashOutAmount,
    decimal NetAmount,
    string FlowType // "IN" | "OUT"
);

public sealed record DailyCashSummaryResponseDto(
    string DateLabel,
    string TableTitleDateLabel,
    decimal OpeningBalanceAmount,
    decimal TotalCashInAmount,
    decimal TotalCashOutAmount,
    decimal ClosingBalanceAmount,
    decimal NetDailyMovementAmount,
    string NetMovementStatusLabel,
    IReadOnlyList<DailyCashMovementBarDto> IncomingBars,
    IReadOnlyList<DailyCashMovementBarDto> OutgoingBars,
    IReadOnlyList<DailyCashCategorySummaryDto> CategoryRows
);

