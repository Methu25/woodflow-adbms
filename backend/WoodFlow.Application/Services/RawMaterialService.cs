using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class RawMaterialService(IRawMaterialRepository repository)
{
    public static string ComputeStatus(decimal onHand, decimal reorder)
    {
        if (onHand <= (reorder * 0.50m)) return "CRITICAL";
        if (onHand <= reorder) return "LOW STOCK";
        return "HEALTHY";
    }

    private static RawMaterialDto ToDto(RawMaterial m) => new(
        m.MaterialId,
        m.MaterialCode,
        m.Name,
        m.Category,
        m.UnitOfMeasure,
        m.QuantityOnHand,
        m.ReorderLevel,
        m.UnitCost,
        m.ValuationLkr,
        m.StorageLocation,
        m.Notes,
        m.IsActive,
        m.IsLowStock,
        m.IsCriticalStock,
        ComputeStatus(m.QuantityOnHand, m.ReorderLevel),
        m.UpdatedAt
    );

    public async Task<RawStockDashboardResponse> GetDashboardDataAsync(string? category, string? search, CancellationToken ct = default)
    {
        var summary = await repository.GetStockSummaryAsync(ct);
        var materials = await repository.ListMaterialsAsync(category, search, includeInactive: false, ct);
        return new RawStockDashboardResponse(
            summary.TotalSkus,
            summary.TotalValuationLkr,
            summary.LowStockCount,
            summary.CriticalStockCount,
            summary.ActiveSuppliersCount,
            materials.Select(ToDto).ToList()
        );
    }

    public async Task<IReadOnlyList<RawMaterialDto>> ListMaterialsAsync(string? category, string? search, bool includeInactive, CancellationToken ct = default)
    {
        var materials = await repository.ListMaterialsAsync(category, search, includeInactive, ct);
        return materials.Select(ToDto).ToList();
    }

    public async Task<RawMaterialDto?> GetByIdAsync(Guid materialId, CancellationToken ct = default)
    {
        var m = await repository.FindByIdAsync(materialId, ct);
        return m is null ? null : ToDto(m);
    }

    public async Task<RawMaterialDto> CreateMaterialAsync(CreateRawMaterialRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.MaterialCode))
            throw new ArgumentException("Material code is required.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Material name is required.");
        if (req.QuantityOnHand < 0)
            throw new ArgumentException("Quantity on hand cannot be negative.");
        if (req.ReorderLevel < 0)
            throw new ArgumentException("Reorder level cannot be negative.");
        if (req.UnitCost < 0)
            throw new ArgumentException("Unit cost cannot be negative.");

        var existing = await repository.FindByCodeAsync(req.MaterialCode.Trim(), ct);
        if (existing is not null)
            throw new InvalidOperationException($"Material code '{req.MaterialCode}' already exists.");

        var material = new RawMaterial(
            Guid.NewGuid(),
            req.MaterialCode.Trim().ToUpperInvariant(),
            req.Name.Trim(),
            req.Category,
            req.UnitOfMeasure,
            req.QuantityOnHand,
            req.ReorderLevel,
            req.UnitCost,
            string.IsNullOrWhiteSpace(req.StorageLocation) ? "Main Yard · Bay A" : req.StorageLocation.Trim(),
            req.Notes?.Trim(),
            IsActive: true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        var created = await repository.CreateAsync(material, req.PreferredSupplierId, req.LeadTimeDays, ct);
        return ToDto(created);
    }

    public async Task<RawMaterialDto?> UpdateMaterialAsync(Guid id, UpdateRawMaterialRequest req, CancellationToken ct = default)
    {
        var existing = await repository.FindByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Material name is required.");
        if (req.ReorderLevel < 0)
            throw new ArgumentException("Reorder level cannot be negative.");
        if (req.UnitCost < 0)
            throw new ArgumentException("Unit cost cannot be negative.");

        var updated = existing with
        {
            Name = req.Name.Trim(),
            Category = req.Category,
            UnitOfMeasure = req.UnitOfMeasure,
            ReorderLevel = req.ReorderLevel,
            UnitCost = req.UnitCost,
            StorageLocation = string.IsNullOrWhiteSpace(req.StorageLocation) ? existing.StorageLocation : req.StorageLocation.Trim(),
            Notes = req.Notes?.Trim(),
            IsActive = req.IsActive,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await repository.UpdateAsync(updated, ct);
        return result is null ? null : ToDto(result);
    }

    public async Task<GoodsReceiptDto> PostGoodsReceiptAsync(PostGoodsReceiptRequest req, CancellationToken ct = default)
    {
        if (req.QuantityReceived <= 0)
            throw new ArgumentException("Received quantity must be greater than zero.");
        if (req.UnitCost < 0)
            throw new ArgumentException("Unit cost cannot be negative.");

        var grn = await repository.RecordGoodsReceiptAsync(
            req.MaterialId,
            req.SupplierId,
            req.QuantityReceived,
            req.UnitCost,
            req.MoisturePct,
            string.IsNullOrWhiteSpace(req.ReceivedBy) ? "Workshop Foreman" : req.ReceivedBy.Trim(),
            req.Notes?.Trim(),
            ct
        );

        return new GoodsReceiptDto(
            grn.ReceiptId,
            grn.GrnNumber,
            grn.MaterialId,
            grn.MaterialCode,
            grn.MaterialName,
            grn.SupplierId,
            grn.SupplierName,
            grn.QuantityReceived,
            grn.UnitOfMeasure,
            grn.UnitCost,
            grn.MoisturePct,
            grn.ReceivedBy,
            grn.Notes,
            grn.ReceivedAt
        );
    }

    public async Task<IReadOnlyList<GoodsReceiptDto>> ListGoodsReceiptsAsync(int limit = 50, CancellationToken ct = default)
    {
        var grns = await repository.ListGoodsReceiptsAsync(limit, ct);
        return grns.Select(g => new GoodsReceiptDto(
            g.ReceiptId,
            g.GrnNumber,
            g.MaterialId,
            g.MaterialCode,
            g.MaterialName,
            g.SupplierId,
            g.SupplierName,
            g.QuantityReceived,
            g.UnitOfMeasure,
            g.UnitCost,
            g.MoisturePct,
            g.ReceivedBy,
            g.Notes,
            g.ReceivedAt
        )).ToList();
    }

    public async Task<JobMaterialDto> IssueMaterialToJobAsync(IssueMaterialRequest req, CancellationToken ct = default)
    {
        if (req.QuantityIssued <= 0)
            throw new ArgumentException("Issued quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(req.JobId))
            throw new ArgumentException("Workshop Job ID is required.");

        var record = await repository.RecordConsumptionAsync(
            req.JobId.Trim(),
            req.MaterialId,
            req.QuantityIssued,
            string.IsNullOrWhiteSpace(req.ArtisanName) ? "Senior Artisan" : req.ArtisanName.Trim(),
            req.Notes?.Trim(),
            ct
        );

        return new JobMaterialDto(
            record.JobMaterialId,
            record.JobId,
            record.MaterialId,
            record.MaterialCode,
            record.MaterialName,
            record.UnitOfMeasure,
            record.QuantityIssued,
            record.ArtisanName,
            record.Notes,
            record.IssuedAt,
            record.TriggerImpact
        );
    }

    public async Task<IReadOnlyList<JobMaterialDto>> ListConsumptionsAsync(int limit = 50, CancellationToken ct = default)
    {
        var list = await repository.ListConsumptionsAsync(limit, ct);
        return list.Select(c => new JobMaterialDto(
            c.JobMaterialId,
            c.JobId,
            c.MaterialId,
            c.MaterialCode,
            c.MaterialName,
            c.UnitOfMeasure,
            c.QuantityIssued,
            c.ArtisanName,
            c.Notes,
            c.IssuedAt,
            c.TriggerImpact
        )).ToList();
    }

    public async Task<IReadOnlyList<LowStockReorderItem>> ListLowStockReorderQueueAsync(CancellationToken ct = default)
    {
        return await repository.ListLowStockReorderQueueAsync(ct);
    }
}
