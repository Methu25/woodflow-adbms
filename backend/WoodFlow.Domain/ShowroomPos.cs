namespace WoodFlow.Domain;

public static class PosPaymentModes
{
    public const string Cash = "CASH";
    public const string Card = "CARD";
    public const string BankTransfer = "BANK_TRANSFER";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Cash,
        Card,
        BankTransfer
    };
}

public static class PosInvoiceStatuses
{
    public const string Paid = "PAID";
    public const string Pending = "PENDING";
    public const string Voided = "VOIDED";
    public const string Refunded = "REFUNDED";
}

public sealed record ShowroomPosProduct(
    int ProductId,
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
    string ImageKey,
    bool IsActive
);

public sealed record PosCustomerSummary(
    int CustomerId,
    string FullName,
    string PhoneNumber,
    string? WhatsappNumber,
    string? DeliveryAddress,
    DateTimeOffset? CreatedAt
);

public sealed record ShowroomInvoiceLine(
    int ItemId,
    int InvoiceId,
    int ProductId,
    string SkuCode,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal,
    bool IsRefunded
);

public sealed record ShowroomInvoiceRecord(
    int InvoiceId,
    string InvoiceNumber,
    int? CustomerId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerAddress,
    decimal SubtotalAmount,
    string? DiscountCode,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TotalAmount,
    string PaymentMode,
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
    IReadOnlyList<ShowroomInvoiceLine> Items
);

public sealed record PosActiveOrderSummary(
    string OrderNumber,
    string CustomerName,
    string ItemSummary,
    decimal AgreedPrice,
    decimal DepositPaid,
    string Status
);

public sealed record PosTillSessionRecord(
    int SessionId,
    DateOnly SessionDate,
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
    decimal GrossDayRevenue,
    decimal SafeDepositCash,
    string SessionStatus,
    string? ZReportNumber,
    string? ClosedByName,
    string? VerifiedByName,
    DateTimeOffset? ClosedAt
);
