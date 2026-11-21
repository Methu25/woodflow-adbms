using WoodFlow.Application.DTOs;
using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public sealed record PosComputedLine(
    int ProductId,
    string SkuCode,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal
);

public sealed record PosComputedCheckout(
    int? CustomerId,
    IReadOnlyList<PosComputedLine> Lines,
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
    string TillNumber
);

public interface IPosCounterRepository
{
    Task<IReadOnlyList<ShowroomPosProduct>> ListProductsAsync(string? category, string? search, CancellationToken ct);
    Task<ShowroomPosProduct?> GetProductByIdAsync(int productId, CancellationToken ct);
    Task<IReadOnlyList<PosCustomerSummary>> ListCustomersAsync(string? search, CancellationToken ct);
    Task<PosCustomerSummary?> GetCustomerByIdAsync(int customerId, CancellationToken ct);
    Task<PosWriteResult<PosCustomerSummary>> CreateCustomerAsync(CreatePosCustomerRequest request, CancellationToken ct);
    Task<IReadOnlyList<PosActiveOrderSummary>> ListActiveOrdersAsync(CancellationToken ct);
    Task<IReadOnlyList<ShowroomInvoiceRecord>> ListInvoicesAsync(string? search, string? paymentMode, DateOnly? date, CancellationToken ct);
    Task<ShowroomInvoiceRecord?> GetInvoiceByIdAsync(int invoiceId, CancellationToken ct);
    Task<PosWriteResult<ShowroomInvoiceRecord>> CreateInvoiceAsync(int actorId, string actorName, PosComputedCheckout checkout, CancellationToken ct);
    Task<PosWriteResult<ShowroomInvoiceRecord>> VoidOrRefundInvoiceAsync(int actorId, string actorName, int invoiceId, string reason, IReadOnlyCollection<int>? itemIds, CancellationToken ct);
    Task<PosTillSessionRecord> GetTillSessionAsync(DateOnly date, CancellationToken ct);
    Task<PosTillSessionRecord> SaveTillCountAsync(DateOnly date, TillDenominationRequest request, CancellationToken ct);
    Task<PosWriteResult<PosTillSessionRecord>> CloseTillDayAsync(int actorId, string actorName, DateOnly date, CloseTillDayRequest request, CancellationToken ct);
}
