namespace WoodFlow.Domain;

public sealed record FinishedFurnitureItem(
    int ProductId,
    string SkuCode,
    string Barcode,
    string ItemName,
    string Category,
    string PrimaryWoodType,
    decimal RetailPrice,
    int CurrentStock,
    int MinReorderLevel,
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

public sealed record FinishedFurnitureCategory(
    int CategoryId,
    string CategoryCode,
    string CategoryName,
    string Description,
    bool IsActive,
    int DisplayOrder,
    int SkuCount
);

public sealed record ShowroomStockMovementRecord(
    long MovementId,
    int ProductId,
    string SkuCode,
    string ItemName,
    string MovementType,
    int QuantityChanged,
    int StockBefore,
    int StockAfter,
    string? ReferenceId,
    int? PerformedByUserId,
    string PerformedByName,
    string? Notes,
    DateTimeOffset CreatedAt
);
