using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> ListSuppliersAsync(string? category, string? search, bool includeInactive, CancellationToken ct);
    Task<Supplier?> FindByIdAsync(int supplierId, CancellationToken ct);
    Task<IReadOnlyList<MaterialSupplierMapping>> GetSupplierMaterialsAsync(int supplierId, CancellationToken ct);
    Task<Supplier> CreateAsync(Supplier supplier, int? initialMaterialId, decimal? initialUnitPrice, int? initialLeadTimeDays, CancellationToken ct);
    Task<Supplier?> UpdateAsync(Supplier supplier, CancellationToken ct);
}
