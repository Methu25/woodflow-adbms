using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface IRawMaterialRepository
{
    Task<IReadOnlyList<RawMaterial>> ListMaterialsAsync(string? category, string? search, bool includeInactive, CancellationToken ct);
    Task<RawMaterial?> FindByIdAsync(Guid materialId, CancellationToken ct);
    Task<RawMaterial?> FindByCodeAsync(string materialCode, CancellationToken ct);
    Task<RawMaterial> CreateAsync(RawMaterial material, Guid? preferredSupplierId, int? leadTimeDays, CancellationToken ct);
    Task<RawMaterial?> UpdateAsync(RawMaterial material, CancellationToken ct);
    Task<GoodsReceiptNote> RecordGoodsReceiptAsync(Guid materialId, Guid? supplierId, decimal quantityReceived, decimal unitCost, decimal moisturePct, string receivedBy, string? notes, CancellationToken ct);
    Task<IReadOnlyList<GoodsReceiptNote>> ListGoodsReceiptsAsync(int limit, CancellationToken ct);
    Task<JobMaterialConsumption> RecordConsumptionAsync(string jobId, Guid materialId, decimal quantityIssued, string artisanName, string? notes, CancellationToken ct);
    Task<IReadOnlyList<JobMaterialConsumption>> ListConsumptionsAsync(int limit, CancellationToken ct);
    Task<IReadOnlyList<LowStockReorderItem>> ListLowStockReorderQueueAsync(CancellationToken ct);
    Task<RawMaterialStockSummary> GetStockSummaryAsync(CancellationToken ct);
}
