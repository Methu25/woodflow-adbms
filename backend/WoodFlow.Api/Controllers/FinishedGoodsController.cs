using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/finished-goods")]
[Route("api/inventory/finished")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FinishedGoodsController(FinishedFurnitureService service) : ControllerBase
{
    [Authorize(Policy = "inventory.view")]
    [HttpGet]
    public async Task<ActionResult<FinishedCatalogResponseDto>> List(
        CancellationToken ct,
        string? category = null,
        string? search = null,
        bool includeInactive = false) =>
        Ok(await service.GetCatalogAsync(category, search, includeInactive, ct));

    [Authorize(Policy = "inventory.view")]
    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        await service.GetByIdAsync(id, ct) is { } item
            ? Ok(item)
            : NotFound(new { message = "Furniture item not found." });

    [Authorize(Policy = "inventory.view")]
    [HttpGet("{skuCode}/availability")]
    public async Task<IActionResult> Availability(string skuCode, CancellationToken ct) =>
        await service.GetAvailabilityBySkuAsync(skuCode, ct) is { } availability
            ? Ok(availability)
            : NotFound(new { message = $"SKU '{skuCode}' not found in showroom catalog." });

    [Authorize(Policy = "inventory.create")]
    [HttpPost]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Create(UpsertFinishedFurnitureRequest request, CancellationToken ct) =>
        MapResult(await service.CreateItemAsync(ActorId, ActorName, request, ct), created: true);

    [Authorize(Policy = "inventory.edit")]
    [HttpPut("{id:int:min(1)}")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Update(int id, UpsertFinishedFurnitureRequest request, CancellationToken ct) =>
        MapResult(await service.UpdateItemAsync(ActorId, ActorName, id, request, ct));

    [Authorize(Policy = "inventory.delete")]
    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await service.DeleteItemAsync(ActorId, id, ct);
        return result.Error == FinishedFurnitureWriteError.None
            ? NoContent()
            : NotFound(new { message = result.Message ?? "Furniture item not found." });
    }

    [Authorize(Policy = "inventory.view")]
    [HttpGet("movements")]
    public async Task<ActionResult<StockMovementLedgerResponseDto>> ListMovements(
        CancellationToken ct,
        int? productId = null,
        string? filterType = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        Ok(await service.GetMovementsAsync(productId, filterType, from, to, ct));

    [Authorize(Policy = "inventory.edit")]
    [HttpPost("movements")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> RecordMovement(RecordStockMovementRequest request, CancellationToken ct) =>
        MapResult(await service.RecordMovementAsync(ActorId, ActorName, request, ct));

    [Authorize(Policy = "inventory.edit")]
    [HttpPost("audit")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> ApplyAudit(ApplyStockAuditRequest request, CancellationToken ct) =>
        MapResult(await service.ApplyStockAuditAsync(ActorId, ActorName, request, ct));

    [Authorize(Policy = "inventory.view")]
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<FinishedCategoryDto>>> ListCategories(CancellationToken ct) =>
        Ok(await service.GetCategoriesAsync(ct));

    [Authorize(Policy = "inventory.create")]
    [HttpPost("categories")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> CreateCategory(CreateCategoryRequest request, CancellationToken ct) =>
        MapResult(await service.CreateCategoryAsync(request, ct), created: true);

    [Authorize(Policy = "inventory.edit")]
    [HttpPut("categories/{categoryId:int:min(1)}")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> UpdateCategory(int categoryId, UpdateCategoryRequest request, CancellationToken ct) =>
        MapResult(await service.UpdateCategoryAsync(categoryId, request, ct));

    [Authorize(Policy = "inventory.view")]
    [HttpGet("alerts")]
    public async Task<ActionResult<LowStockAlertsResponseDto>> ListAlerts(CancellationToken ct, string? filter = null) =>
        Ok(await service.GetLowStockAlertsAsync(filter, ct));

    [Authorize(Policy = "inventory.edit")]
    [HttpPost("alerts/restock")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> RequestRestock(RequestWorkshopRestockRequest request, CancellationToken ct) =>
        MapResult(await service.RequestRestockAsync(ActorId, ActorName, request, ct));

    [Authorize(Policy = "inventory.edit")]
    [HttpPost("alerts/batch-restock")]
    public async Task<IActionResult> RequestBatchRestock(CancellationToken ct)
    {
        var count = await service.RequestBatchRestockAsync(ActorId, ActorName, ct);
        return Ok(new { updatedCount = count, message = $"Batch restock request submitted for {count} low-stock showroom SKUs." });
    }

    private int ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 1;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "P Rejishanth";

    private IActionResult MapResult<T>(FinishedFurnitureWriteResult<T> result, bool created = false) => result.Error switch
    {
        FinishedFurnitureWriteError.None => created ? StatusCode(201, result.Data) : Ok(result.Data),
        FinishedFurnitureWriteError.NotFound => NotFound(new { message = result.Message ?? "Record not found." }),
        FinishedFurnitureWriteError.DuplicateSku or
        FinishedFurnitureWriteError.DuplicateBarcode or
        FinishedFurnitureWriteError.DuplicateCategory or
        FinishedFurnitureWriteError.InsufficientStock => Conflict(new { message = result.Message ?? "Conflict while saving inventory record." }),
        _ => BadRequest(new { message = result.Message ?? "Invalid inventory request." })
    };
}
