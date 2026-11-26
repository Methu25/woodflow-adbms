namespace WoodFlow.Application.DTOs;

public sealed record SupplierDto(
    int SupplierId,
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
    IReadOnlyList<SupplierMaterialMappingDto> ContractedMaterials
);

public sealed record SupplierMaterialMappingDto(
    int MaterialId,
    string MaterialCode,
    string MaterialName,
    decimal UnitPrice,
    int LeadTimeDays,
    bool IsPreferred
);

public sealed record CreateSupplierRequest(
    string SupplierCode,
    string CompanyName,
    string ContactPerson,
    string Phone,
    string Email,
    string Address,
    string PrimaryCategory,
    int? InitialMaterialId,
    decimal? InitialUnitPrice,
    int? InitialLeadTimeDays
);

public sealed record UpdateSupplierRequest(
    string CompanyName,
    string ContactPerson,
    string Phone,
    string Email,
    string Address,
    string PrimaryCategory,
    bool IsActive
);
