using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> ListSuppliersAsync(string? category, string? search, bool includeInactive, CancellationToken ct);
    Task<Supplier?> FindByIdAsync(Guid supplierId, CancellationToken ct);
    Task<IReadOnlyList<MaterialSupplierMapping>> GetSupplierMaterialsAsync(Guid supplierId, CancellationToken ct);
    Task<Supplier> CreateAsync(Supplier supplier, Guid? initialMaterialId, decimal? initialUnitPrice, int? initialLeadTimeDays, CancellationToken ct);
    Task<Supplier?> UpdateAsync(Supplier supplier, CancellationToken ct);
}
