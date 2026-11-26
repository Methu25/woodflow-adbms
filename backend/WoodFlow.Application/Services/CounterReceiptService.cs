using System.Text.RegularExpressions;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class CounterReceiptService(IPosCounterRepository repository)
{
    private const string DefaultTillNumber = "Till #01";
    private const string DefaultOwnerVerifier = "Wimalasiri (Owner)";

    public static string NormalizeCategoryName(string category) => category switch
    {
        "Bed" or "Wardrobe" => "Bedroom",
        "Dining Set" => "Dining Room",
        "Sofa" => "Living Room",
        "Office & Study" => "Office",
        _ => category
    };

    public static PosProductDto MapProduct(ShowroomPosProduct item)
    {
        var category = NormalizeCategoryName(item.Category);
        var (status, label) = item.CurrentStock <= 0
            ? ("OUT_OF_STOCK", "Out: 0 in stock")
            : item.CurrentStock <= item.MinReorderLevel
                ? ("LOW_STOCK", $"Low: {item.CurrentStock} in stock")
                : ("IN_STOCK", $"Floor: {item.CurrentStock} in stock");

        var imageUrl = ResolveProductImageUrl(item.SkuCode, item.ImageKey, category);

        return new PosProductDto(
            item.ProductId,
            item.SkuCode,
            item.Barcode,
            item.ItemName,
            category,
            item.RetailPrice,
            item.CurrentStock,
            item.MinReorderLevel,
            status,
            label,
            item.ImageKey,
            imageUrl
        );
    }

    public static string ResolveProductImageUrl(string skuCode, string? imageKey, string category)
    {
        var key = (imageKey ?? string.Empty).Trim().ToLowerInvariant();
        if (skuCode.Contains("WAR", StringComparison.OrdinalIgnoreCase) || skuCode.Contains("BED-012", StringComparison.OrdinalIgnoreCase) || key is "wardrobe" or "almirah")
            return "/assets/pos/teak-wardrobe.png";
        if (skuCode.Contains("BED", StringComparison.OrdinalIgnoreCase) || key is "bed" or "queenbed" || category.Equals("Bedroom", StringComparison.OrdinalIgnoreCase))
            return "/assets/pos/king-teak-bed.png";
        if (skuCode.Contains("DIN", StringComparison.OrdinalIgnoreCase) || key is "dining" || category.Equals("Dining Room", StringComparison.OrdinalIgnoreCase))
            return "/assets/pos/dining-table.png";
        return "/assets/pos/lounge-suite.png";
    }

    public static PosCustomerDto MapCustomer(PosCustomerSummary customer)
    {
        var initial = string.IsNullOrWhiteSpace(customer.FullName)
            ? "W"
            : char.ToUpperInvariant(customer.FullName.Trim()[0]).ToString();
        var tag = string.IsNullOrWhiteSpace(customer.DeliveryAddress) ||
                  customer.DeliveryAddress.Contains("Walk-in", StringComparison.OrdinalIgnoreCase)
            ? "Walk-in"
            : "Delivery";
        return new PosCustomerDto(
            customer.CustomerId,
            customer.FullName,
            customer.PhoneNumber,
            customer.WhatsappNumber,
            customer.DeliveryAddress,
            initial,
            $"{customer.PhoneNumber} · {tag}"
        );
    }

    public static PosInvoiceDto MapInvoice(ShowroomInvoiceRecord record)
    {
        var initial = string.IsNullOrWhiteSpace(record.CustomerName)
            ? "W"
            : char.ToUpperInvariant(record.CustomerName.Trim()[0]).ToString();
        var modeLabel = record.PaymentMode.ToUpperInvariant() switch
        {
            PosPaymentModes.Card => "Card",
            PosPaymentModes.BankTransfer => "Bank Slip",
            _ => "Cash"
        };

        var digits = Regex.Replace(record.CustomerPhone ?? string.Empty, "[^0-9]", "");
        if (digits.StartsWith("0") && digits.Length == 10)
            digits = "94" + digits[1..];
        if (string.IsNullOrWhiteSpace(digits))
            digits = "94771234567";

        var message = Uri.EscapeDataString(
            $"Thank you from Wimalasiri Furniture, Kottawa! Receipt #{record.InvoiceNumber} · Paid Rs. {record.TotalAmount:N2} ({modeLabel}).");
        var whatsappUrl = $"https://wa.me/{digits}?text={message}";

        return new PosInvoiceDto(
            record.InvoiceId,
            record.InvoiceNumber,
            record.CustomerId,
            record.CustomerName,
            record.CustomerPhone ?? string.Empty,
            record.CustomerAddress,
            initial,
            record.SubtotalAmount,
            record.DiscountCode,
            record.DiscountPercent,
            record.DiscountAmount,
            record.TotalAmount,
            record.PaymentMode,
            modeLabel,
            record.PaymentReference,
            record.CashReceived,
            record.ChangeDue,
            record.InvoiceStatus,
            record.IsCustomOrderDeposit,
            record.TillNumber,
            record.BilledBy,
            record.BilledByName,
            record.InvoiceDate,
            record.VoidReason,
            record.VoidedAt,
            record.Items.Select(i => new PosInvoiceItemDto(
                i.ItemId, i.ProductId, i.SkuCode, i.ItemName, i.Quantity, i.UnitPrice, i.Subtotal, i.IsRefunded)).ToList(),
            whatsappUrl
        );
    }

    public static PosTillSummaryDto MapTillSession(PosTillSessionRecord s)
    {
        var varianceStatus = s.VarianceAmount == 0m
            ? "BALANCED"
            : s.VarianceAmount > 0m
                ? "OVERAGE"
                : "SHORTAGE";

        var zReport = string.IsNullOrWhiteSpace(s.ZReportNumber)
            ? $"ZR-{s.SessionDate:yyyy-MMdd}-01"
            : s.ZReportNumber!;

        return new PosTillSummaryDto(
            s.SessionId,
            s.SessionDate.ToString("yyyy-MM-dd"),
            s.SessionDate.ToString("ddd, dd MMM yyyy"),
            s.TillNumber,
            s.OpeningFloat,
            s.OpenedAt,
            s.CashInvoiceCount,
            s.CashSalesAmount,
            s.CardSlipCount,
            s.CardSalesAmount,
            s.BankSlipCount,
            s.BankSlipAmount,
            s.Notes5000Count,
            s.Notes1000Count,
            s.Notes500Count,
            s.Notes100Count,
            s.Coins50Count,
            s.CountedCashAmount,
            s.ExpectedCashAmount,
            s.VarianceAmount,
            varianceStatus,
            s.GrossDayRevenue,
            s.SafeDepositCash,
            s.SessionStatus,
            zReport,
            s.ClosedByName ?? "Yohan Methusael E J",
            s.VerifiedByName ?? DefaultOwnerVerifier,
            s.ClosedAt
        );
    }

    public static decimal ComputeCountedCash(int notes5000, int notes1000, int notes500, int notes100, int coins50) =>
        (notes5000 * 5000m) + (notes1000 * 1000m) + (notes500 * 500m) + (notes100 * 100m) + (coins50 * 50m);

    public async Task<PosCounterContextResponseDto> GetCounterContextAsync(string? category, string? search, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var allProducts = await repository.ListProductsAsync(null, null, ct);
        var mappedAll = allProducts.Select(MapProduct).ToList();

        var filtered = mappedAll.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase) && !category.Equals("All Items", StringComparison.OrdinalIgnoreCase))
        {
            var norm = NormalizeCategoryName(category.Trim());
            filtered = filtered.Where(p => p.Category.Equals(norm, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(p =>
                p.ItemName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.SkuCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var customers = (await repository.ListCustomersAsync(null, ct)).Select(MapCustomer).ToList();
        var activeOrders = (await repository.ListActiveOrdersAsync(ct))
            .Select(o => new PosActiveOrderDto(o.OrderNumber, o.CustomerName, o.ItemSummary, o.AgreedPrice, o.DepositPaid, o.Status))
            .ToList();
        var till = await repository.GetTillSessionAsync(today, ct);
        var lowStockCount = mappedAll.Count(p => p.CurrentStock <= p.MinReorderLevel);

        return new PosCounterContextResponseDto(
            filtered.ToList(),
            customers,
            activeOrders,
            till.TillNumber,
            till.SessionStatus,
            till.OpeningFloat,
            lowStockCount
        );
    }

    public async Task<IReadOnlyList<PosCustomerDto>> SearchCustomersAsync(string? search, CancellationToken ct) =>
        (await repository.ListCustomersAsync(search, ct)).Select(MapCustomer).ToList();

    public async Task<PosWriteResult<PosCustomerDto>> CreateCustomerAsync(CreatePosCustomerRequest request, CancellationToken ct)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var phone = (request.PhoneNumber ?? string.Empty).Trim();
        if (fullName.Length < 2 || fullName.Length > 100)
            return new(Error: PosWriteError.Validation, Message: "Customer full name must be between 2 and 100 characters.");
        if (phone.Length < 7 || phone.Length > 20)
            return new(Error: PosWriteError.Validation, Message: "Enter a valid customer phone number.");

        var normalized = new CreatePosCustomerRequest(
            fullName,
            phone,
            string.IsNullOrWhiteSpace(request.WhatsappNumber) ? phone : request.WhatsappNumber.Trim(),
            string.IsNullOrWhiteSpace(request.DeliveryAddress) ? "Walk-in · Kottawa Showroom" : request.DeliveryAddress.Trim()
        );

        var saved = await repository.CreateCustomerAsync(normalized, ct);
        return saved.Error == PosWriteError.None && saved.Data is not null
            ? new(MapCustomer(saved.Data))
            : new(Error: saved.Error, Message: saved.Message);
    }

    public async Task<PosWriteResult<PosInvoiceDto>> CreateInvoiceAsync(
        int actorId,
        string actorName,
        bool isOwner,
        CreatePosInvoiceRequest request,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var till = await repository.GetTillSessionAsync(today, ct);
        if (string.Equals(till.SessionStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Error: PosWriteError.TillClosed,
                Message: $"Register {till.TillNumber} is closed and locked for today. All POS checkouts are blocked until the next opening float is verified.");
        }

        if (request.Items is null || request.Items.Count == 0)
            return new(Error: PosWriteError.Validation, Message: "Add at least one furniture item to the current bill before checkout.");

        var mode = (request.PaymentMode ?? string.Empty).Trim().ToUpperInvariant();
        if (!PosPaymentModes.All.Contains(mode))
            return new(Error: PosWriteError.Validation, Message: "Payment mode must be CASH, CARD, or BANK_TRANSFER.");

        var groupedItems = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        if (groupedItems.Any(i => i.ProductId <= 0 || i.Quantity <= 0 || i.Quantity > 100))
            return new(Error: PosWriteError.Validation, Message: "Each line item must specify a valid product and positive quantity.");

        var computedLines = new List<PosComputedLine>(groupedItems.Count);
        foreach (var (productId, qty) in groupedItems)
        {
            var product = await repository.GetProductByIdAsync(productId, ct);
            if (product is null || !product.IsActive)
                return new(Error: PosWriteError.NotFound, Message: $"Furniture product #{productId} was not found in the active showroom catalog.");

            if (product.CurrentStock < qty)
            {
                return new(
                    Error: PosWriteError.InsufficientStock,
                    Message: $"Database trigger trg_pos_stock_deduct aborted checkout: {product.ItemName} ({product.SkuCode}) only has {product.CurrentStock} unit(s) available.");
            }

            var lineSubtotal = Math.Round(product.RetailPrice * qty, 2, MidpointRounding.AwayFromZero);
            computedLines.Add(new PosComputedLine(
                product.ProductId,
                product.SkuCode,
                product.ItemName,
                qty,
                product.RetailPrice,
                lineSubtotal));
        }

        if (request.CustomerId is > 0)
        {
            var customer = await repository.GetCustomerByIdAsync(request.CustomerId.Value, ct);
            if (customer is null)
                return new(Error: PosWriteError.NotFound, Message: "Selected customer record was not found.");
        }

        var subtotal = computedLines.Sum(l => l.Subtotal);

        // Enforce Owner-Only discount rule (Audit Note #3)
        var requestedDiscountPct = Math.Max(0m, request.DiscountPercent);
        var requestedDiscountAmt = Math.Max(0m, request.DiscountAmount);
        if ((requestedDiscountPct > 0m || requestedDiscountAmt > 0m) && !isOwner)
        {
            return new(
                Error: PosWriteError.OwnerAuthorizationRequired,
                Message: "Showroom counter discounts require Owner authorization.");
        }

        if (requestedDiscountPct > 50m)
            return new(Error: PosWriteError.Validation, Message: "Counter discount percentage cannot exceed 50%.");

        var discountAmount = requestedDiscountPct > 0m
            ? Math.Round(subtotal * (requestedDiscountPct / 100m), 2, MidpointRounding.AwayFromZero)
            : Math.Round(requestedDiscountAmt, 2, MidpointRounding.AwayFromZero);

        if (discountAmount > subtotal)
            return new(Error: PosWriteError.Validation, Message: "Discount amount cannot exceed bill subtotal.");

        var netAfterDiscount = subtotal - discountAmount;
        var totalPayable = request.IsCustomOrderDeposit
            ? Math.Round(netAfterDiscount / 3m, 2, MidpointRounding.AwayFromZero)
            : netAfterDiscount;

        string? reference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim();
        decimal cashReceived = 0m;
        decimal changeDue = 0m;

        if (mode == PosPaymentModes.Cash)
        {
            cashReceived = request.CashReceived > 0m ? Math.Round(request.CashReceived, 2, MidpointRounding.AwayFromZero) : totalPayable;
            if (cashReceived < totalPayable)
            {
                return new(
                    Error: PosWriteError.Validation,
                    Message: $"Cash received (Rs. {cashReceived:N2}) is less than total payable (Rs. {totalPayable:N2}).");
            }
            changeDue = cashReceived - totalPayable;
            reference ??= DefaultTillNumber;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length < 4)
            {
                return new(
                    Error: PosWriteError.Validation,
                    Message: mode == PosPaymentModes.Card
                        ? "Card POS terminal approval reference number is required."
                        : "Bank Slip deposit reference number is required.");
            }
            cashReceived = totalPayable;
        }

        var discountCode = discountAmount > 0m
            ? (string.IsNullOrWhiteSpace(request.DiscountCode) ? $"DISC-{Math.Round(requestedDiscountPct):0}" : request.DiscountCode.Trim())
            : null;

        var checkout = new PosComputedCheckout(
            request.CustomerId,
            computedLines,
            subtotal,
            discountCode,
            requestedDiscountPct,
            discountAmount,
            totalPayable,
            mode,
            reference,
            cashReceived,
            changeDue,
            PosInvoiceStatuses.Paid,
            request.IsCustomOrderDeposit,
            DefaultTillNumber
        );

        var saved = await repository.CreateInvoiceAsync(actorId, actorName, checkout, ct);
        return saved.Error == PosWriteError.None && saved.Data is not null
            ? new(MapInvoice(saved.Data))
            : new(Error: saved.Error, Message: saved.Message);
    }

    public async Task<PosInvoiceHistoryResponseDto> ListInvoicesAsync(
        string? search,
        string? paymentMode,
        DateOnly? date,
        CancellationToken ct)
    {
        var allRecords = await repository.ListInvoicesAsync(null, null, null, ct);
        var filteredRecords = await repository.ListInvoicesAsync(search, paymentMode, date, ct);

        return new PosInvoiceHistoryResponseDto(
            filteredRecords.Select(MapInvoice).ToList(),
            allRecords.Count,
            allRecords.Count(r => r.InvoiceStatus == PosInvoiceStatuses.Paid),
            allRecords.Count(r => r.InvoiceStatus == PosInvoiceStatuses.Pending),
            allRecords.Count(r => r.InvoiceStatus is PosInvoiceStatuses.Voided or PosInvoiceStatuses.Refunded)
        );
    }

    public async Task<PosInvoiceDto?> GetInvoiceByIdAsync(int invoiceId, CancellationToken ct) =>
        await repository.GetInvoiceByIdAsync(invoiceId, ct) is { } record ? MapInvoice(record) : null;

    public async Task<PosWriteResult<PosInvoiceDto>> VoidOrRefundInvoiceAsync(
        int actorId,
        string actorName,
        bool isOwner,
        int invoiceId,
        VoidRefundInvoiceRequest request,
        CancellationToken ct)
    {
        if (!isOwner)
        {
            return new(
                Error: PosWriteError.OwnerAuthorizationRequired,
                Message: "Only an Owner account can authorize invoice voids or refunds.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var till = await repository.GetTillSessionAsync(today, ct);
        if (string.Equals(till.SessionStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Error: PosWriteError.TillClosed,
                Message: $"Register {till.TillNumber} is closed and locked. Voids and refunds are blocked until tomorrow's opening float.");
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 4 || reason.Length > 300)
            return new(Error: PosWriteError.Validation, Message: "Provide a clear refund or void reason (minimum 4 characters).");

        var pin = (request.OwnerPin ?? string.Empty).Trim();
        if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
            return new(Error: PosWriteError.InvalidOwnerPin, Message: "Enter a valid 4–8 digit Owner Security PIN to confirm void/refund.");

        var itemIds = request.ItemIds is { Count: > 0 } ? request.ItemIds : null;
        var updated = await repository.VoidOrRefundInvoiceAsync(actorId, actorName, invoiceId, reason, itemIds, ct);
        return updated.Error == PosWriteError.None && updated.Data is not null
            ? new(MapInvoice(updated.Data))
            : new(Error: updated.Error, Message: updated.Message);
    }

    public async Task<PosTillSummaryDto> GetTillSummaryAsync(DateOnly? date, CancellationToken ct)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        return MapTillSession(await repository.GetTillSessionAsync(targetDate, ct));
    }

    public async Task<PosWriteResult<PosTillSummaryDto>> UpdateTillCountAsync(
        DateOnly? date,
        TillDenominationRequest request,
        CancellationToken ct)
    {
        if (request.Notes5000Count < 0 || request.Notes1000Count < 0 || request.Notes500Count < 0 ||
            request.Notes100Count < 0 || request.Coins50Count < 0)
        {
            return new(Error: PosWriteError.Validation, Message: "Denomination note/coin counts cannot be negative.");
        }

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var current = await repository.GetTillSessionAsync(targetDate, ct);
        if (string.Equals(current.SessionStatus, "CLOSED", StringComparison.OrdinalIgnoreCase))
            return new(Error: PosWriteError.TillClosed, Message: "Till session is already locked for this date.");

        var updated = await repository.SaveTillCountAsync(targetDate, request, ct);
        return new(MapTillSession(updated));
    }

    public async Task<PosWriteResult<PosTillSummaryDto>> CloseTillDayAsync(
        int actorId,
        string actorName,
        DateOnly? date,
        CloseTillDayRequest request,
        CancellationToken ct)
    {
        if (request.Notes5000Count < 0 || request.Notes1000Count < 0 || request.Notes500Count < 0 ||
            request.Notes100Count < 0 || request.Coins50Count < 0)
        {
            return new(Error: PosWriteError.Validation, Message: "Denomination note/coin counts cannot be negative.");
        }

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var closed = await repository.CloseTillDayAsync(actorId, actorName, targetDate, request, ct);
        return closed.Error == PosWriteError.None && closed.Data is not null
            ? new(MapTillSession(closed.Data))
            : new(Error: closed.Error, Message: closed.Message);
    }
}
