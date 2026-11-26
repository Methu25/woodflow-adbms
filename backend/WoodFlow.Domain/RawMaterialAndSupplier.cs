namespace WoodFlow.Domain;

public sealed record RawMaterial(
    Guid MaterialId,
    string MaterialCode,
    string Name,
    string Category,
    string UnitOfMeasure,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal UnitCost,
    string StorageLocation,
    string? Notes,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt
)
{
    public bool IsLowStock => QuantityOnHand <= ReorderLevel;
    public bool IsCriticalStock => QuantityOnHand <= (ReorderLevel * 0.50m);
    public decimal ValuationLkr => QuantityOnHand * UnitCost;
}

public sealed record Supplier(
    Guid SupplierId,
    string SupplierCode,
    string CompanyName,
    string ContactPerson,
    string Phone,
    string Email,
    string Address,
    string PrimaryCategory,
    bool IsActive,
    decimal Rating,
    int MaterialsSuppliedCount,
    decimal AvgLeadTimeDays,
    DateTimeOffset CreatedAt
);

public sealed record MaterialSupplierMapping(
    Guid MaterialId,
    Guid SupplierId,
    string MaterialName,
    string MaterialCode,
    decimal UnitPrice,
    int LeadTimeDays,
    bool IsPreferred
);

public sealed record GoodsReceiptNote(
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

public sealed record JobMaterialConsumption(
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

public sealed record LowStockReorderItem(
    Guid MaterialId,
    string MaterialCode,
    string Name,
    string Category,
    string UnitOfMeasure,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    decimal DeficitQuantity,
    decimal SuggestedOrderQty,
    decimal UnitCost,
    string? PreferredSupplierName,
    string? PreferredSupplierPhone,
    int? LeadTimeDays,
    string Severity
);

public sealed record RawMaterialStockSummary(
    int TotalSkus,
    decimal TotalValuationLkr,
    int LowStockCount,
    int CriticalStockCount,
    int ActiveSuppliersCount
);
