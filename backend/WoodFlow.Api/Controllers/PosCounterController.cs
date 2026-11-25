using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/pos")]
[Route("api/v1/pos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PosCounterController(CounterReceiptService service) : ControllerBase
{
    [Authorize(Policy = "sales.view")]
    [HttpGet("context")]
    public async Task<ActionResult<PosCounterContextResponseDto>> GetContext(
        CancellationToken ct,
        string? category = null,
        string? search = null) =>
        Ok(await service.GetCounterContextAsync(category, search, ct));

    [Authorize(Policy = "sales.view")]
    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<PosProductDto>>> ListProducts(
        CancellationToken ct,
        string? category = null,
        string? search = null)
    {
        var context = await service.GetCounterContextAsync(category, search, ct);
        return Ok(context.Products);
    }

    [Authorize(Policy = "sales.view")]
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<PosCustomerDto>>> ListCustomers(
        CancellationToken ct,
        string? search = null) =>
        Ok(await service.SearchCustomersAsync(search, ct));

    [Authorize(Policy = "sales.create")]
    [HttpPost("customers")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> CreateCustomer(CreatePosCustomerRequest request, CancellationToken ct) =>
        MapResult(await service.CreateCustomerAsync(request, ct), created: true);

    [Authorize(Policy = "sales.view")]
    [HttpGet("invoices")]
    public async Task<ActionResult<PosInvoiceHistoryResponseDto>> ListInvoices(
        CancellationToken ct,
        string? search = null,
        string? paymentMode = null,
        DateOnly? date = null) =>
        Ok(await service.ListInvoicesAsync(search, paymentMode, date, ct));

    [Authorize(Policy = "sales.view")]
    [HttpGet("invoices/{id:int:min(1)}")]
    public async Task<IActionResult> GetInvoice(int id, CancellationToken ct) =>
        await service.GetInvoiceByIdAsync(id, ct) is { } invoice
            ? Ok(invoice)
            : NotFound(new { message = "Counter invoice not found." });

    [Authorize(Policy = "sales.create")]
    [HttpPost("invoices")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> CreateInvoice(CreatePosInvoiceRequest request, CancellationToken ct) =>
        MapResult(await service.CreateInvoiceAsync(ActorId, ActorName, IsOwner, request, ct), created: true);

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [HttpPost("invoices/{id:int:min(1)}/void")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> VoidOrRefundInvoice(int id, VoidRefundInvoiceRequest request, CancellationToken ct) =>
        MapResult(await service.VoidOrRefundInvoiceAsync(ActorId, ActorName, IsOwner, id, request, ct));

    [Authorize(Policy = "sales.view")]
    [HttpGet("till/current")]
    public async Task<ActionResult<PosTillSummaryDto>> GetCurrentTill(CancellationToken ct, DateOnly? date = null) =>
        Ok(await service.GetTillSummaryAsync(date, ct));

    [Authorize(Policy = "sales.create")]
    [HttpPut("till/count")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> UpdateTillCount(TillDenominationRequest request, CancellationToken ct, DateOnly? date = null) =>
        MapResult(await service.UpdateTillCountAsync(date, request, ct));

    [Authorize(Policy = "sales.create")]
    [HttpPost("till/close")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> CloseTillDay(CloseTillDayRequest request, CancellationToken ct, DateOnly? date = null) =>
        MapResult(await service.CloseTillDayAsync(ActorId, ActorName, date, request, ct));

    private int ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 1;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "Yohan Methusael E J";
    private bool IsOwner => User.IsInRole(Roles.Owner);

    private IActionResult MapResult<T>(PosWriteResult<T> result, bool created = false) => result.Error switch
    {
        PosWriteError.None => created ? StatusCode(201, result.Data) : Ok(result.Data),
        PosWriteError.NotFound => NotFound(new { message = result.Message ?? "Record not found." }),
        PosWriteError.OwnerAuthorizationRequired => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Message ?? "Owner authorization required." }),
        PosWriteError.InsufficientStock or
        PosWriteError.DuplicatePhone or
        PosWriteError.TillClosed or
        PosWriteError.AlreadyVoided => Conflict(new { error = result.Error.ToString(), message = result.Message ?? "POS conflict occurred." }),
        _ => BadRequest(new { error = result.Error.ToString(), message = result.Message ?? "Invalid POS request." })
    };
}
