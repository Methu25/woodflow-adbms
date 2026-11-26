using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class SupplierService(ISupplierRepository repository)
{
    private static async Task<SupplierDto> ToDtoAsync(Supplier s, ISupplierRepository repo, CancellationToken ct)
    {
        var mappings = await repo.GetSupplierMaterialsAsync(s.SupplierId, ct);
        return new SupplierDto(
            s.SupplierId,
            s.SupplierCode,
            s.CompanyName,
            s.ContactPerson,
            s.Phone,
            s.Email,
            s.Address,
            s.PrimaryCategory,
            s.IsActive,
            s.Rating,
            s.MaterialsSuppliedCount,
            s.AvgLeadTimeDays,
            mappings.Select(m => new SupplierMaterialMappingDto(
                m.MaterialId,
                m.MaterialCode,
                m.MaterialName,
                m.UnitPrice,
                m.LeadTimeDays,
                m.IsPreferred
            )).ToList()
        );
    }

    public async Task<IReadOnlyList<SupplierDto>> ListSuppliersAsync(string? category, string? search, bool includeInactive, CancellationToken ct = default)
    {
        var suppliers = await repository.ListSuppliersAsync(category, search, includeInactive, ct);
        var dtos = new List<SupplierDto>();
        foreach (var s in suppliers)
        {
            dtos.Add(await ToDtoAsync(s, repository, ct));
        }
        return dtos;
    }

    public async Task<SupplierDto?> GetByIdAsync(Guid supplierId, CancellationToken ct = default)
    {
        var s = await repository.FindByIdAsync(supplierId, ct);
        return s is null ? null : await ToDtoAsync(s, repository, ct);
    }

    public async Task<SupplierDto> CreateSupplierAsync(CreateSupplierRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SupplierCode))
            throw new ArgumentException("Supplier code is required.");
        if (string.IsNullOrWhiteSpace(req.CompanyName))
            throw new ArgumentException("Company name is required.");

        var supplier = new Supplier(
            Guid.NewGuid(),
            req.SupplierCode.Trim().ToUpperInvariant(),
            req.CompanyName.Trim(),
            req.ContactPerson.Trim(),
            req.Phone.Trim(),
            req.Email.Trim(),
            req.Address.Trim(),
            string.IsNullOrWhiteSpace(req.PrimaryCategory) ? "Timber" : req.PrimaryCategory.Trim(),
            IsActive: true,
            Rating: 4.50m,
            MaterialsSuppliedCount: req.InitialMaterialId.HasValue ? 1 : 0,
            AvgLeadTimeDays: req.InitialLeadTimeDays ?? 3,
            DateTimeOffset.UtcNow
        );

        var created = await repository.CreateAsync(supplier, req.InitialMaterialId, req.InitialUnitPrice, req.InitialLeadTimeDays, ct);
        return await ToDtoAsync(created, repository, ct);
    }

    public async Task<SupplierDto?> UpdateSupplierAsync(Guid id, UpdateSupplierRequest req, CancellationToken ct = default)
    {
        var existing = await repository.FindByIdAsync(id, ct);
        if (existing is null) return null;

        var updated = existing with
        {
            CompanyName = req.CompanyName.Trim(),
            ContactPerson = req.ContactPerson.Trim(),
            Phone = req.Phone.Trim(),
            Email = req.Email.Trim(),
            Address = req.Address.Trim(),
            PrimaryCategory = req.PrimaryCategory.Trim(),
            IsActive = req.IsActive
        };

        var result = await repository.UpdateAsync(updated, ct);
        return result is null ? null : await ToDtoAsync(result, repository, ct);
    }
}
