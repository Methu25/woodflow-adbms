using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SuppliersController(SupplierService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> List(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var suppliers = await service.ListSuppliersAsync(category, search, includeInactive, ct);
        return Ok(suppliers);
    }

    [HttpGet("{id:int:min(1)}")]
    [Authorize(Policy = "inventory.view")]
    public async Task<ActionResult<SupplierDto>> GetById(int id, CancellationToken ct)
    {
        var supplier = await service.GetByIdAsync(id, ct);
        return supplier is null ? NotFound(new { message = "Supplier not found." }) : Ok(supplier);
    }

    [HttpPost]
    [Authorize(Policy = "inventory.create")]
    [RequestSizeLimit(16384)]
    public async Task<ActionResult<SupplierDto>> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken ct)
    {
        try
        {
            var created = await service.CreateSupplierAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.SupplierId }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int:min(1)}")]
    [Authorize(Policy = "inventory.edit")]
    [RequestSizeLimit(16384)]
    public async Task<ActionResult<SupplierDto>> Update(
        int id,
        [FromBody] UpdateSupplierRequest request,
        CancellationToken ct)
    {
        var updated = await service.UpdateSupplierAsync(id, request, ct);
        return updated is null ? NotFound(new { message = "Supplier not found." }) : Ok(updated);
    }
}
