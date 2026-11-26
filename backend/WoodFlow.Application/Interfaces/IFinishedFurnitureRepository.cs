using WoodFlow.Application.DTOs;
using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public enum FinishedFurnitureWriteError
{
    None,
    NotFound,
    DuplicateSku,
    DuplicateBarcode,
    DuplicateCategory,
    InsufficientStock,
    ValidationFailed
}

public sealed record FinishedFurnitureWriteResult<T>(
    T? Data = default,
    FinishedFurnitureWriteError Error = FinishedFurnitureWriteError.None,
    string? Message = null
);

public interface IFinishedFurnitureRepository
{
    Task<IReadOnlyList<FinishedFurnitureItem>> ListItemsAsync(string? category, string? search, bool includeInactive, CancellationToken ct);
    Task<FinishedFurnitureItem?> FindByIdAsync(int productId, CancellationToken ct);
    Task<FinishedFurnitureItem?> FindBySkuAsync(string skuCode, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> CreateItemAsync(int actorId, string actorName, UpsertFinishedFurnitureRequest request, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> UpdateItemAsync(int actorId, string actorName, int productId, UpsertFinishedFurnitureRequest request, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<bool>> DeleteItemAsync(int actorId, int productId, CancellationToken ct);

    Task<IReadOnlyList<ShowroomStockMovementRecord>> ListMovementsAsync(int? productId, string? filterType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<ShowroomStockMovementRecord>> RecordMovementAsync(int actorId, string actorName, RecordStockMovementRequest request, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<StockAuditSessionResultDto>> ApplyStockAuditAsync(int actorId, string actorName, ApplyStockAuditRequest request, CancellationToken ct);

    Task<IReadOnlyList<FinishedFurnitureCategory>> ListCategoriesAsync(CancellationToken ct);
    Task<FinishedFurnitureWriteResult<FinishedFurnitureCategory>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
    Task<FinishedFurnitureWriteResult<FinishedFurnitureCategory>> UpdateCategoryAsync(int categoryId, UpdateCategoryRequest request, CancellationToken ct);

    Task<FinishedFurnitureWriteResult<FinishedFurnitureItem>> RequestRestockAsync(int actorId, string actorName, RequestWorkshopRestockRequest request, CancellationToken ct);
    Task<int> RequestBatchRestockAsync(int actorId, string actorName, CancellationToken ct);
}
