namespace WoodFlow.Application.DTOs;

public sealed record PosProductDto(
    int ProductId,
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
    string StockStatus,
    string StockBadgeLabel,
    string ImageKey,
    string ImageUrl
);

public sealed record PosCustomerDto(
    int CustomerId,
    string FullName,
    string PhoneNumber,
    string? WhatsappNumber,
    string? DeliveryAddress,
    string Initial,
    string Subtitle
);

public sealed record PosActiveOrderDto(
    string OrderNumber,
    string CustomerName,
    string ItemSummary,
    decimal AgreedPrice,
    decimal DepositPaid,
    string Status
);

public sealed record PosCounterContextResponseDto(
    IReadOnlyList<PosProductDto> Products,
    IReadOnlyList<PosCustomerDto> Customers,
    IReadOnlyList<PosActiveOrderDto> ActiveOrders,
    string TillNumber,
    string TillStatus,
    decimal OpeningFloat,
    int LowStockCount
);

public sealed record CreatePosCustomerRequest(
    string FullName,
    string PhoneNumber,
    string? WhatsappNumber,
    string? DeliveryAddress
);

public sealed record CreatePosInvoiceItemRequest(
    int ProductId,
    int Quantity
);

public sealed record CreatePosInvoiceRequest(
    int? CustomerId,
    IReadOnlyList<CreatePosInvoiceItemRequest> Items,
    string PaymentMode,
    string? PaymentReference,
    decimal CashReceived,
    string? DiscountCode,
    decimal DiscountPercent,
    decimal DiscountAmount,
    bool IsCustomOrderDeposit = false
);

public sealed record PosInvoiceItemDto(
    int ItemId,
    int ProductId,
    string SkuCode,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal,
    bool IsRefunded
);

public sealed record PosInvoiceDto(
    int InvoiceId,
    string InvoiceNumber,
    int? CustomerId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerAddress,
    string CustomerInitial,
    decimal SubtotalAmount,
    string? DiscountCode,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TotalAmount,
    string PaymentMode,
    string PaymentModeLabel,
    string? PaymentReference,
    decimal CashReceived,
    decimal ChangeDue,
    string InvoiceStatus,
    bool IsCustomOrderDeposit,
    string TillNumber,
    int? BilledBy,
    string BilledByName,
    DateTimeOffset InvoiceDate,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    IReadOnlyList<PosInvoiceItemDto> Items,
    string WhatsappShareUrl
);

public sealed record PosInvoiceHistoryResponseDto(
    IReadOnlyList<PosInvoiceDto> Invoices,
    int TotalCount,
    int PaidCount,
    int PendingCount,
    int VoidedOrRefundedCount
);

public sealed record VoidRefundInvoiceRequest(
    string Reason,
    string OwnerPin,
    IReadOnlyList<int>? ItemIds = null
);

public sealed record TillDenominationRequest(
    int Notes5000Count,
    int Notes1000Count,
    int Notes500Count,
    int Notes100Count,
    int Coins50Count
);

public sealed record CloseTillDayRequest(
    int Notes5000Count,
    int Notes1000Count,
    int Notes500Count,
    int Notes100Count,
    int Coins50Count,
    string? VerifiedByName = null
);

public sealed record PosTillSummaryDto(
    int SessionId,
    string SessionDate,
    string DisplayDate,
    string TillNumber,
    decimal OpeningFloat,
    string OpenedAt,
    int CashInvoiceCount,
    decimal CashSalesAmount,
    int CardSlipCount,
    decimal CardSalesAmount,
    int BankSlipCount,
    decimal BankSlipAmount,
    int Notes5000Count,
    int Notes1000Count,
    int Notes500Count,
    int Notes100Count,
    int Coins50Count,
    decimal CountedCashAmount,
    decimal ExpectedCashAmount,
    decimal VarianceAmount,
    string VarianceStatus,
    decimal GrossDayRevenue,
    decimal SafeDepositCash,
    string SessionStatus,
    string ZReportNumber,
    string ClosedByName,
    string VerifiedByName,
    DateTimeOffset? ClosedAt
);

public enum PosWriteError
{
    None,
    Validation,
    NotFound,
    InsufficientStock,
    DuplicatePhone,
    TillClosed,
    OwnerAuthorizationRequired,
    InvalidOwnerPin,
    AlreadyVoided
}

public sealed record PosWriteResult<T>(
    T? Data = default,
    PosWriteError Error = PosWriteError.None,
    string? Message = null
);
