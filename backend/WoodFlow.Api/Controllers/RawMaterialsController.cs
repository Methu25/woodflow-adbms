using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/raw-materials")]
public sealed class RawMaterialsController(RawMaterialService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<RawStockDashboardResponse>> GetDashboard(
        [FromQuery] string? category,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var data = await service.GetDashboardDataAsync(category, search, ct);
        return Ok(data);
    }

    [HttpGet("catalog")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<IReadOnlyList<RawMaterialDto>>> ListMaterials(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var items = await service.ListMaterialsAsync(category, search, includeInactive, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<RawMaterialDto>> GetById(Guid id, CancellationToken ct)
    {
        var material = await service.GetByIdAsync(id, ct);
        return material is null ? NotFound(new { message = "Raw material not found." }) : Ok(material);
    }

    [HttpPost]
    [Authorize(Policy = "inventory.create")]
    public async Task<ActionResult<RawMaterialDto>> Create(
        [FromBody] CreateRawMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var created = await service.CreateMaterialAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.MaterialId }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "inventory.edit")]
    public async Task<ActionResult<RawMaterialDto>> Update(
        Guid id,
        [FromBody] UpdateRawMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await service.UpdateMaterialAsync(id, request, ct);
            return updated is null ? NotFound(new { message = "Raw material not found." }) : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("grn")]
    [Authorize(Policy = "inventory.create")]
    public async Task<ActionResult<GoodsReceiptDto>> PostGoodsReceipt(
        [FromBody] PostGoodsReceiptRequest request,
        CancellationToken ct)
    {
        try
        {
            var grn = await service.PostGoodsReceiptAsync(request, ct);
            return Ok(grn);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("grn")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<IReadOnlyList<GoodsReceiptDto>>> ListGoodsReceipts(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var list = await service.ListGoodsReceiptsAsync(limit, ct);
        return Ok(list);
    }

    [HttpPost("consumption")]
    [Authorize(Policy = "inventory.edit")]
    public async Task<ActionResult<JobMaterialDto>> IssueMaterial(
        [FromBody] IssueMaterialRequest request,
        CancellationToken ct)
    {
        try
        {
            var issued = await service.IssueMaterialToJobAsync(request, ct);
            return Ok(issued);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("consumption")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<IReadOnlyList<JobMaterialDto>>> ListConsumptions(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var list = await service.ListConsumptionsAsync(limit, ct);
        return Ok(list);
    }

    [HttpGet("low-stock")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<IReadOnlyList<LowStockReorderItem>>> GetLowStockQueue(CancellationToken ct)
    {
        var queue = await service.ListLowStockReorderQueueAsync(ct);
        return Ok(queue);
    }
}
