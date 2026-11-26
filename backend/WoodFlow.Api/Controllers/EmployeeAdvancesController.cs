using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/finance/advances")]
[Route("api/advances")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeAdvancesController(EmployeeAdvanceService service) : ControllerBase
{
    [Authorize]
    [HttpGet("issuance-context")]
    public async Task<ActionResult<AdvanceIssuanceContextResponseDto>> GetIssuanceContext(CancellationToken ct) =>
        Ok(await service.GetIssuanceContextAsync(ct));

    [Authorize]
    [HttpGet("ledger")]
    public async Task<ActionResult<EmployeeAdvanceLedgerResponseDto>> GetLedger(
        CancellationToken ct,
        string? filter = null,
        string? search = null) =>
        Ok(await service.GetAdvanceLedgerAsync(filter, search, ct));

    [Authorize]
    [HttpGet("employees/{employeeId:int:min(1)}")]
    public async Task<IActionResult> GetEmployeeProfile(int employeeId, CancellationToken ct) =>
        await service.GetEmployeeProfileAsync(employeeId, ct) is { } profile
            ? Ok(profile)
            : NotFound(new { message = "Employee advance profile not found." });

    [Authorize]
    [HttpPost]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> IssueAdvance(IssueEmployeeAdvanceRequest request, CancellationToken ct)
    {
        var result = await service.IssueAdvanceAsync(ActorId, ActorName, request, ct);
        return result.Error switch
        {
            EmployeeAdvanceWriteError.None => StatusCode(201, result.Data),
            EmployeeAdvanceWriteError.NotFound => NotFound(new { message = result.Message ?? "Employee not found." }),
            EmployeeAdvanceWriteError.DuplicateVoucher => Conflict(new { message = result.Message ?? "Duplicate voucher reference." }),
            EmployeeAdvanceWriteError.SupervisorOverrideRequired => UnprocessableEntity(new { message = result.Message ?? "Supervisor override approval is required for deficit advances." }),
            _ => BadRequest(new { message = result.Message ?? "Invalid salary advance request." })
        };
    }

    [Authorize]
    [HttpPost("settle")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> SettleAdvance(SettleEmployeeAdvanceRequest request, CancellationToken ct)
    {
        var result = await service.SettleAdvanceAsync(ActorId, ActorName, request, ct);
        return result.Error switch
        {
            EmployeeAdvanceWriteError.None => Ok(result.Data),
            EmployeeAdvanceWriteError.NotFound => NotFound(new { message = result.Message ?? "Employee not found." }),
            _ => BadRequest(new { message = result.Message ?? "Unable to settle employee advance." })
        };
    }

    private int ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 1;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "SM Asfaq";
}
