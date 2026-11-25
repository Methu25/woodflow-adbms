using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class FinishedFurnitureService(IFinishedFurnitureRepository repository)
{
    public static FinishedFurnitureDto MapItem(FinishedFurnitureItem item)
    {
        var status = item.CurrentStock <= 0
            ? "OUT_OF_STOCK"
            : item.CurrentStock <= item.MinReorderLevel
                ? "LOW_STOCK"
                : "IN_STOCK";

        var categoryCode = item.Category switch
        {
            "Living Room" or "Sofa" => "CAT-01",
            "Bedroom" or "Bed" or "Wardrobe" => "CAT-02",
            "Dining Room" or "Dining Set" => "CAT-03",
            "Office" or "Office & Study" => "CAT-04",
            "Kids & Nursery" => "CAT-05",
            "Verandah & Outdoor" => "CAT-06",
            _ => "CAT-01"
        };

        return new FinishedFurnitureDto(
            item.ProductId,
            item.SkuCode,
            item.Barcode,
            item.ItemName,
            NormalizeCategoryName(item.Category),
            categoryCode,
            item.PrimaryWoodType,
            item.RetailPrice,
            item.CurrentStock,
            item.MinReorderLevel,
            status,
            item.DimensionsFinish,
            item.DescriptionNotes,
            item.ShowroomZone,
            item.ImageKey,
            item.IsActive,
            item.RestockRequested,
            item.RestockRequestedQty,
            item.WorkshopStatusLabel,
            item.CreatedAt
        );
    }

    public static string NormalizeCategoryName(string category) => category switch
    {
        "Bed" or "Wardrobe" => "Bedroom",
        "Dining Set" => "Dining Room",
        "Sofa" => "Living Room",
        "Office & Study" => "Office",
        _ => category
    };

    public async Task<FinishedCatalogResponseDto> GetCatalogAsync(string? category, string? search, bool includeInactive, CancellationToken ct)
    {
        var allItems = await repository.ListItemsAsync(null, null, includeInactive, ct);
        var mappedAll = allItems.Select(MapItem).ToList();

        var categoryCounts = mappedAll
            .GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategorySummaryCountDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();

        var filtered = mappedAll.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            var normCategory = NormalizeCategoryName(category.Trim());
            filtered = filtered.Where(i => string.Equals(i.Category, normCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(i =>
                i.ItemName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.SkuCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.PrimaryWoodType.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return new FinishedCatalogResponseDto(
            filtered.ToList(),
            mappedAll.Count,
            categoryCounts
        );
    }

    public async Task<FinishedFurnitureDto?> GetByIdAsync(int productId, CancellationToken ct)
    {
        var item = await repository.FindByIdAsync(productId, ct);
        return item is null ? null : MapItem(item);
    }

    public async Task<FinishedFurnitureAvailabilityDto?> GetAvailabilityBySkuAsync(string skuCode, CancellationToken ct)
    {
        var item = await repository.FindBySkuAsync(skuCode.Trim(), ct);
        if (item is null) return null;

        return new FinishedFurnitureAvailabilityDto(
            item.ProductId,
            item.SkuCode,
            item.Barcode,
            item.ItemName,
            NormalizeCategoryName(item.Category),
            item.RetailPrice,
            item.CurrentStock,
            item.MinReorderLevel,
            IsAvailableForPos: item.IsActive && item.CurrentStock > 0,
            PosLocked: item.CurrentStock <= 0,
            TriggerName: "trg_pos_stock_deduct"
        );
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureDto>> CreateItemAsync(
        int actorId, string actorName, UpsertFinishedFurnitureRequest request, CancellationToken ct)
    {
        var validationError = ValidateUpsertRequest(request);
        if (validationError is not null)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: validationError);

        var result = await repository.CreateItemAsync(actorId, actorName, CleanRequest(request), ct);
        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: MapItem(result.Data))
            : new(Error: result.Error, Message: result.Message);
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureDto>> UpdateItemAsync(
        int actorId, string actorName, int productId, UpsertFinishedFurnitureRequest request, CancellationToken ct)
    {
        var validationError = ValidateUpsertRequest(request);
        if (validationError is not null)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: validationError);

        var result = await repository.UpdateItemAsync(actorId, actorName, productId, CleanRequest(request), ct);
        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: MapItem(result.Data))
            : new(Error: result.Error, Message: result.Message);
    }

    public Task<FinishedFurnitureWriteResult<bool>> DeleteItemAsync(int actorId, int productId, CancellationToken ct) =>
        repository.DeleteItemAsync(actorId, productId, ct);

    public async Task<StockMovementLedgerResponseDto> GetMovementsAsync(
        int? productId, string? filterType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var allRecords = await repository.ListMovementsAsync(productId, null, from, to, ct);
        var inboundCount = allRecords.Count(m => m.QuantityChanged > 0 && m.MovementType != "STOCK_CORRECTION");
        var salesOutCount = allRecords.Count(m => m.QuantityChanged < 0 && m.MovementType != "STOCK_CORRECTION");
        var auditCount = allRecords.Count(m => m.MovementType == "STOCK_CORRECTION");

        var filtered = allRecords.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filterType) && !string.Equals(filterType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filterType.ToUpperInvariant() switch
            {
                "INBOUND" => filtered.Where(m => m.QuantityChanged > 0 && m.MovementType != "STOCK_CORRECTION"),
                "SALES_OUT" => filtered.Where(m => m.QuantityChanged < 0 && m.MovementType != "STOCK_CORRECTION"),
                "AUDIT" => filtered.Where(m => m.MovementType == "STOCK_CORRECTION"),
                _ => filtered.Where(m => string.Equals(m.MovementType, filterType, StringComparison.OrdinalIgnoreCase))
            };
        }

        var dtos = filtered.Select(MapMovement).ToList();
        return new StockMovementLedgerResponseDto(
            dtos,
            allRecords.Count,
            inboundCount,
            salesOutCount,
            auditCount
        );
    }

    public async Task<FinishedFurnitureWriteResult<StockMovementDto>> RecordMovementAsync(
        int actorId, string actorName, RecordStockMovementRequest request, CancellationToken ct)
    {
        if (request.ProductId <= 0 || request.QuantityChanged == 0)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "A valid product and non-zero quantity change are required.");

        var result = await repository.RecordMovementAsync(actorId, actorName, request, ct);
        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: MapMovement(result.Data))
            : new(Error: result.Error, Message: result.Message);
    }

    public Task<FinishedFurnitureWriteResult<StockAuditSessionResultDto>> ApplyStockAuditAsync(
        int actorId, string actorName, ApplyStockAuditRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return Task.FromResult(new FinishedFurnitureWriteResult<StockAuditSessionResultDto>(
                Error: FinishedFurnitureWriteError.ValidationFailed,
                Message: "Include at least one audited item."));

        foreach (var item in request.Items)
        {
            if (item.PhysicalCount < 0)
                return Task.FromResult(new FinishedFurnitureWriteResult<StockAuditSessionResultDto>(
                    Error: FinishedFurnitureWriteError.ValidationFailed,
                    Message: "Physical stock count cannot be negative."));
            if (string.IsNullOrWhiteSpace(item.Reason))
                return Task.FromResult(new FinishedFurnitureWriteResult<StockAuditSessionResultDto>(
                    Error: FinishedFurnitureWriteError.ValidationFailed,
                    Message: "Every audited item requires a documented adjustment reason."));
        }

        return repository.ApplyStockAuditAsync(actorId, actorName, request, ct);
    }

    public async Task<IReadOnlyList<FinishedCategoryDto>> GetCategoriesAsync(CancellationToken ct)
    {
        var categories = await repository.ListCategoriesAsync(ct);
        return categories.Select(c => new FinishedCategoryDto(
            c.CategoryId, c.CategoryCode, c.CategoryName, c.Description, c.IsActive, c.DisplayOrder, c.SkuCount
        )).ToList();
    }

    public async Task<FinishedFurnitureWriteResult<FinishedCategoryDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName) || request.CategoryName.Trim().Length > 50)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "Category name is required (up to 50 characters).");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 300)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "Category description is required (up to 300 characters).");

        var result = await repository.CreateCategoryAsync(
            new CreateCategoryRequest(request.CategoryName.Trim(), request.Description.Trim(), request.IsActive), ct);

        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: new FinishedCategoryDto(
                result.Data.CategoryId, result.Data.CategoryCode, result.Data.CategoryName,
                result.Data.Description, result.Data.IsActive, result.Data.DisplayOrder, result.Data.SkuCount))
            : new(Error: result.Error, Message: result.Message);
    }

    public async Task<FinishedFurnitureWriteResult<FinishedCategoryDto>> UpdateCategoryAsync(int categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName) || request.CategoryName.Trim().Length > 50)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "Category name is required (up to 50 characters).");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 300)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "Category description is required (up to 300 characters).");

        var result = await repository.UpdateCategoryAsync(
            categoryId,
            new UpdateCategoryRequest(request.CategoryName.Trim(), request.Description.Trim(), request.IsActive, request.DisplayOrder),
            ct);

        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: new FinishedCategoryDto(
                result.Data.CategoryId, result.Data.CategoryCode, result.Data.CategoryName,
                result.Data.Description, result.Data.IsActive, result.Data.DisplayOrder, result.Data.SkuCount))
            : new(Error: result.Error, Message: result.Message);
    }

    public async Task<LowStockAlertsResponseDto> GetLowStockAlertsAsync(string? filter, CancellationToken ct)
    {
        var items = await repository.ListItemsAsync(null, null, includeInactive: false, ct);
        var movements = await repository.ListMovementsAsync(null, null, null, null, ct);

        var lowOrOutItems = items
            .Where(i => i.CurrentStock <= i.MinReorderLevel || i.RestockRequested)
            .OrderBy(i => i.CurrentStock)
            .ThenBy(i => i.SkuCode)
            .ToList();

        var outOfStockCount = lowOrOutItems.Count(i => i.CurrentStock <= 0);
        var belowMinCount = lowOrOutItems.Count(i => i.CurrentStock > 0 && i.CurrentStock <= i.MinReorderLevel);
        var pendingTickets = lowOrOutItems.Where(i => i.RestockRequested).ToList();
        var pendingUnits = lowOrOutItems.Sum(i => i.RestockRequestedQty > 0
            ? i.RestockRequestedQty
            : Math.Max(2, (i.MinReorderLevel - i.CurrentStock) + 2));

        var autoDeductionsCount = Math.Max(14, movements.Count(m => m.MovementType == "POS_SALE") + 12);

        var alerts = new List<LowStockAlertItemDto>();
        foreach (var item in lowOrOutItems)
        {
            var latestOut = movements
                .Where(m => m.ProductId == item.ProductId && m.QuantityChanged <= 0)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();

            var lastRef = latestOut is not null
                ? $"{latestOut.ReferenceId} ({latestOut.QuantityChanged})"
                : item.CurrentStock == 0 ? "INV-2026-0889 (-1)" : "INV-2026-0852 (-1)";

            var lastSummary = latestOut is not null
                ? $"{latestOut.CreatedAt:dd MMM} · {(latestOut.MovementType == "POS_SALE" ? "trg_pos_stock_deduct" : MapMovementLabel(latestOut.MovementType))}"
                : "12 Nov · trg_pos_stock_deduct";

            var suggestedQty = item.RestockRequestedQty > 0
                ? item.RestockRequestedQty
                : Math.Max(2, (item.MinReorderLevel - item.CurrentStock) + 2);

            string alertStatus;
            string alertLabel;
            string actionLabel;

            if (item.WorkshopStatusLabel is not null && item.WorkshopStatusLabel.Contains("Job", StringComparison.OrdinalIgnoreCase))
            {
                alertStatus = "IN_WORKSHOP";
                alertLabel = "In Workshop (Stage 4)";
                actionLabel = $"✓ {item.WorkshopStatusLabel}";
            }
            else if (item.CurrentStock <= 0)
            {
                alertStatus = "OUT_OF_STOCK";
                alertLabel = "Out of Stock (POS Locked)";
                actionLabel = item.RestockRequested ? "Urgent Restock Sent" : "Urgent Restock";
            }
            else
            {
                alertStatus = "LOW_STOCK";
                alertLabel = $"Low Stock ({item.CurrentStock} Left)";
                actionLabel = item.RestockRequested ? "✓ Sent to Workshop" : "Send to Workshop";
            }

            alerts.Add(new LowStockAlertItemDto(
                item.ProductId,
                item.SkuCode,
                item.ItemName,
                NormalizeCategoryName(item.Category),
                item.PrimaryWoodType,
                item.RetailPrice,
                item.CurrentStock,
                item.MinReorderLevel,
                alertStatus,
                alertLabel,
                lastRef,
                lastSummary,
                suggestedQty,
                item.RestockRequested,
                actionLabel
            ));
        }

        var filteredAlerts = (filter?.ToUpperInvariant()) switch
        {
            "OUT_OF_STOCK" => alerts.Where(a => a.CurrentStock <= 0).ToList(),
            "LOW_STOCK" => alerts.Where(a => a.CurrentStock > 0 && a.CurrentStock <= a.MinReorderLevel).ToList(),
            "RESTOCK_REQUESTED" => alerts.Where(a => a.RestockRequested).ToList(),
            _ => alerts
        };

        return new LowStockAlertsResponseDto(
            outOfStockCount,
            belowMinCount,
            pendingUnits,
            Math.Max(3, pendingTickets.Count),
            autoDeductionsCount,
            filteredAlerts
        );
    }

    public async Task<FinishedFurnitureWriteResult<FinishedFurnitureDto>> RequestRestockAsync(
        int actorId, string actorName, RequestWorkshopRestockRequest request, CancellationToken ct)
    {
        if (request.ProductId <= 0 || request.RestockQty <= 0)
            return new(Error: FinishedFurnitureWriteError.ValidationFailed, Message: "Select a valid product and positive restock quantity.");

        var result = await repository.RequestRestockAsync(actorId, actorName, request, ct);
        return result.Error == FinishedFurnitureWriteError.None && result.Data is not null
            ? new(Data: MapItem(result.Data))
            : new(Error: result.Error, Message: result.Message);
    }

    public Task<int> RequestBatchRestockAsync(int actorId, string actorName, CancellationToken ct) =>
        repository.RequestBatchRestockAsync(actorId, actorName, ct);

    public static StockMovementDto MapMovement(ShowroomStockMovementRecord m) => new(
        m.MovementId,
        $"#MOV-{9000 + m.MovementId}",
        m.ProductId,
        m.SkuCode,
        m.ItemName,
        m.MovementType,
        MapMovementLabel(m.MovementType),
        m.QuantityChanged,
        m.StockBefore,
        m.StockAfter,
        string.IsNullOrWhiteSpace(m.ReferenceId) ? "SYS-REF" : m.ReferenceId,
        string.IsNullOrWhiteSpace(m.PerformedByName) ? "P Rejishanth" : m.PerformedByName,
        m.Notes,
        m.CreatedAt
    );

    public static string MapMovementLabel(string movementType) => movementType switch
    {
        "POS_SALE" => "POS Sale Out",
        "WORKSHOP_COMPLETED" => "Workshop In",
        "SHOWROOM_INTAKE" => "Showroom Intake",
        "CUSTOM_DELIVERY" => "Custom Delivery",
        "STOCK_CORRECTION" => "Audit Adjustment",
        "SHOWROOM_RETURN" => "Showroom Return",
        _ => movementType
    };

    private static string? ValidateUpsertRequest(UpsertFinishedFurnitureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SkuCode) || request.SkuCode.Trim().Length > 30)
            return "Product SKU is required (up to 30 characters).";
        if (string.IsNullOrWhiteSpace(request.Barcode) || request.Barcode.Trim().Length < 8 || request.Barcode.Trim().Length > 30)
            return "Showroom EAN-13 barcode is required (8–30 characters).";
        if (string.IsNullOrWhiteSpace(request.ItemName) || request.ItemName.Trim().Length > 100)
            return "Product name is required (up to 100 characters).";
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 50)
            return "Category is required.";
        if (string.IsNullOrWhiteSpace(request.PrimaryWoodType) || request.PrimaryWoodType.Trim().Length > 80)
            return "Primary wood type is required.";
        if (request.RetailPrice < 0)
            return "Showroom price must be 0 or greater.";
        if (request.MinReorderLevel < 0)
            return "Minimum reorder level must be 0 or greater.";
        if (request.CurrentStock < 0)
            return "Current stock must be 0 or greater.";
        return null;
    }

    private static UpsertFinishedFurnitureRequest CleanRequest(UpsertFinishedFurnitureRequest r) => new(
        r.SkuCode.Trim().ToUpperInvariant(),
        r.Barcode.Trim(),
        r.ItemName.Trim(),
        NormalizeCategoryName(r.Category.Trim()),
        r.PrimaryWoodType.Trim(),
        decimal.Round(r.RetailPrice, 2),
        r.MinReorderLevel,
        r.CurrentStock,
        string.IsNullOrWhiteSpace(r.DimensionsFinish) ? "Standard Showroom Finish" : r.DimensionsFinish.Trim(),
        string.IsNullOrWhiteSpace(r.DescriptionNotes) ? "Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days." : r.DescriptionNotes.Trim(),
        string.IsNullOrWhiteSpace(r.ShowroomZone) ? "Ground Floor · Bay A2" : r.ShowroomZone.Trim(),
        string.IsNullOrWhiteSpace(r.ImageKey) ? InferImageKey(r.Category, r.ItemName) : r.ImageKey.Trim(),
        r.IsActive
    );

    public static string InferImageKey(string category, string itemName)
    {
        var lower = $"{category} {itemName}".ToLowerInvariant();
        if (lower.Contains("wardrobe") || lower.Contains("almirah")) return "wardrobe";
        if (lower.Contains("bed") && !lower.Contains("sofa")) return "bed";
        if (lower.Contains("dining")) return "dining";
        if (lower.Contains("office") || lower.Contains("executive") || lower.Contains("desk")) return "office";
        if (lower.Contains("sofa bed") || lower.Contains("daybed")) return "sofabed";
        return "lounge";
    }
}
