namespace WoodFlow.Application.DTOs;

public sealed record RawMaterialDto(
    Guid MaterialId,
    string MaterialCode,
    string Name,
    string Category,
    string UnitOfMeasure,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal UnitCost,
    decimal ValuationLkr,
    string StorageLocation,
    string? Notes,
    bool IsActive,
    bool IsLowStock,
    bool IsCriticalStock,
    string StockStatus,
    DateTimeOffset UpdatedAt
);

public sealed record CreateRawMaterialRequest(
    string MaterialCode,
    string Name,
    string Category,
    string UnitOfMeasure,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal UnitCost,
    string StorageLocation,
    string? Notes,
    Guid? PreferredSupplierId,
    int? LeadTimeDays
);

public sealed record UpdateRawMaterialRequest(
    string Name,
    string Category,
    string UnitOfMeasure,
    decimal ReorderLevel,
    decimal UnitCost,
    string StorageLocation,
    string? Notes,
    bool IsActive
);

public sealed record PostGoodsReceiptRequest(
    Guid MaterialId,
    Guid? SupplierId,
    decimal QuantityReceived,
    decimal UnitCost,
    decimal MoisturePct,
    string ReceivedBy,
    string? Notes
);

public sealed record GoodsReceiptDto(
    Guid ReceiptId,
    string GrnNumber,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    Guid? SupplierId,
    string? SupplierName,
    decimal QuantityReceived,
    string UnitOfMeasure,
    decimal UnitCost,
    decimal MoisturePct,
    string ReceivedBy,
    string? Notes,
    DateTimeOffset ReceivedAt
);

public sealed record IssueMaterialRequest(
    string JobId,
    Guid MaterialId,
    decimal QuantityIssued,
    string ArtisanName,
    string? Notes
);

public sealed record JobMaterialDto(
    Guid JobMaterialId,
    string JobId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string UnitOfMeasure,
    decimal QuantityIssued,
    string ArtisanName,
    string? Notes,
    DateTimeOffset IssuedAt,
    string TriggerImpact
);

public sealed record RawStockDashboardResponse(
    int TotalSkus,
    decimal TotalValuationLkr,
    int LowStockCount,
    int CriticalStockCount,
    int ActiveSuppliersCount,
    IReadOnlyList<RawMaterialDto> Materials
);
