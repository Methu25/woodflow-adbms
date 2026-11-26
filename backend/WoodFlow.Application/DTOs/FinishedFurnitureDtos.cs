namespace WoodFlow.Application.DTOs;

public sealed record FinishedFurnitureDto(
    int ProductId,
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    string CategoryCode,
    string PrimaryWoodType,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
    string StockStatus, // "IN_STOCK", "LOW_STOCK", "OUT_OF_STOCK"
    string DimensionsFinish,
    string DescriptionNotes,
    string ShowroomZone,
    string ImageKey,
    bool IsActive,
    bool RestockRequested,
    int RestockRequestedQty,
    string? WorkshopStatusLabel,
    DateTimeOffset? CreatedAt
);

public sealed record CategorySummaryCountDto(
    string CategoryName,
    int Count
);

public sealed record FinishedCatalogResponseDto(
    IReadOnlyList<FinishedFurnitureDto> Items,
    int TotalActiveSkus,
    IReadOnlyList<CategorySummaryCountDto> CategoryCounts
);

public sealed record UpsertFinishedFurnitureRequest(
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    string PrimaryWoodType,
    decimal RetailPrice,
    int MinReorderLevel,
    int CurrentStock,
    string DimensionsFinish,
    string DescriptionNotes,
    string? ShowroomZone = null,
    string? ImageKey = null,
    bool IsActive = true
);

public sealed record FinishedFurnitureAvailabilityDto(
    int ProductId,
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
    bool IsAvailableForPos,
    bool PosLocked,
    string TriggerName
);

public sealed record StockMovementDto(
    long MovementId,
    string MovementCode,
    int ProductId,
    string SkuCode,
    string ItemName,
    string MovementType,
    string MovementLabel,
    int QuantityChanged,
    int StockBefore,
    int StockAfter,
    string ReferenceId,
    string PerformedByName,
    string? Notes,
    DateTimeOffset CreatedAt
);

public sealed record StockMovementLedgerResponseDto(
    IReadOnlyList<StockMovementDto> Movements,
    int TotalCount,
    int InboundCount,
    int SalesOutCount,
    int AuditAdjustmentCount
);

public sealed record RecordStockMovementRequest(
    int ProductId,
    string MovementType, // SHOWROOM_INTAKE, POS_SALE, WORKSHOP_COMPLETED, STOCK_CORRECTION, CUSTOM_DELIVERY, SHOWROOM_RETURN
    int QuantityChanged,
    string? ReferenceId,
    string? Notes
);

public sealed record AuditAdjustmentItemRequest(
    int ProductId,
    int PhysicalCount,
    string Reason
);

public sealed record ApplyStockAuditRequest(
    string SessionCode,
    IReadOnlyList<AuditAdjustmentItemRequest> Items
);

public sealed record StockAuditSessionResultDto(
    string SessionCode,
    int AdjustedItemsCount,
    int NetVariance,
    DateTimeOffset AppliedAt,
    IReadOnlyList<FinishedFurnitureDto> UpdatedItems
);

public sealed record FinishedCategoryDto(
    int CategoryId,
    string CategoryCode,
    string CategoryName,
    string Description,
    bool IsActive,
    int DisplayOrder,
    int SkuCount
);

public sealed record CreateCategoryRequest(
    string CategoryName,
    string Description,
    bool IsActive = true
);

public sealed record UpdateCategoryRequest(
    string CategoryName,
    string Description,
    bool IsActive,
    int? DisplayOrder = null
);

public sealed record LowStockAlertItemDto(
    int ProductId,
    string SkuCode,
    string ItemName,
    string Category,
    string PrimaryWoodType,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
    string AlertStatus, // OUT_OF_STOCK, LOW_STOCK, IN_WORKSHOP
    string AlertLabel,
    string LastTriggerReference,
    string LastTriggerSummary,
    int SuggestedRestockQty,
    bool RestockRequested,
    string WorkshopActionLabel
);

public sealed record LowStockAlertsResponseDto(
    int OutOfStockCount,
    int BelowMinReorderCount,
    int PendingWorkshopUnits,
    int ActiveRestockTickets,
    int AutoDeductionsToday,
    IReadOnlyList<LowStockAlertItemDto> Alerts
);

public sealed record RequestWorkshopRestockRequest(
    int ProductId,
    int RestockQty,
    bool IsUrgent = false
);
