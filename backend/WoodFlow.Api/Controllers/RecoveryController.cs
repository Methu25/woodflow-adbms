using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Controllers;

[ApiController, Route("api/password-recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecoveryController(IRecoveryRepository repository, RecoveryService service) : ControllerBase
{
    [AllowAnonymous, HttpGet("owners"), EnableRateLimiting("recovery-request")]
    public async Task<IActionResult> Owners(CancellationToken ct) => Ok(await repository.OwnersAsync(ct));

    [AllowAnonymous, HttpPost("requests"), RequestSizeLimit(4096), EnableRateLimiting("recovery-request")]
    public async Task<IActionResult> Submit(SubmitRecoveryRequest request, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        await service.SubmitAsync(request, ct);
        // Bound ordinary existence-dependent timing differences; not a constant-time network guarantee.
        var remaining = TimeSpan.FromMilliseconds(500) - timer.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        return Accepted(new { message = "If the request is eligible, the selected owner will receive it." });
    }

    [AllowAnonymous, HttpPost("complete"), RequestSizeLimit(4096), EnableRateLimiting("recovery-reset")]
    public async Task<IActionResult> Complete(CompleteRecoveryRequest request, CancellationToken ct) =>
        await service.CompleteAsync(request, ct) ? NoContent() : BadRequest(new { message = "Unable to reset password." });

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpGet("requests")]
    public async Task<IActionResult> Inbox(CancellationToken ct, string? status = null, int page = 1, int pageSize = 10, string? category = null)
    {
        if (category is not (null or "pending" or "completed" or "denied")) return BadRequest(new { message = "Invalid category." });
        if (status is not (null or "PENDING" or "APPROVED" or "COMPLETED" or "DENIED" or "EXPIRED") ||
            page is < 1 or > 100000 || pageSize is < 1 or > 50) return BadRequest(new { message = "Invalid search or page." });
        // OWNER work categories do not alter the reset-code security lifecycle.
        status = category switch { "pending" => "PENDING", "completed" => "APPROVED,COMPLETED,EXPIRED", "denied" => "DENIED", _ => status };
        return Result(await repository.InboxAsync(ActorId, status, page, pageSize, ct));
    }

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpGet("pending-count")]
    public async Task<IActionResult> Count(CancellationToken ct)
    {
        var result = await repository.PendingCountAsync(ActorId, ct);
        return result.Error == RecoveryError.None ? Ok(new { pendingCount = result.Value }) : Result(result);
    }

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpGet("requests/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) => Result(await repository.DetailsAsync(ActorId, id, ct));

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpPost("requests/{id:guid}/deny"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Deny(Guid id, CancellationToken ct)
    {
        var result = await repository.ProcessAsync(ActorId, id, null, ct);
        return result.Error == RecoveryError.None ? NoContent() : Result(result);
    }

    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpPost("requests/{id:guid}/approve"), RequestSizeLimit(4096),
     EnableRateLimiting("recovery-approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveRecoveryRequest request, CancellationToken ct) =>
        Result(await service.ApproveAsync(ActorId, id, request.IdentityVerified, ct));

    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly), HttpPost("resolve-pending"), RequestSizeLimit(4096),
     EnableRateLimiting("recovery-approve")]
    public async Task<IActionResult> ResolvePending(ResolvePendingRecoveryRequest request, CancellationToken ct)
    {
        if (!request.IdentityVerified || !request.Confirmed || string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { message = "Verify identity and confirm a valid resolution." });
        var error = await repository.ResolvePendingAsync(ActorId, request.Username.Trim(), request.Resolution == "DENY", ct);
        return error == RecoveryError.None ? NoContent() : Result(new RecoveryResult<object>(Error: error));
    }
    private IActionResult Result<T>(RecoveryResult<T> result) => result.Error switch
    {
        RecoveryError.None => Ok(result.Value),
        RecoveryError.Forbidden => StatusCode(403, new { message = "Owner access is required." }),
        RecoveryError.NotFound => NotFound(new { message = "Request not found." }),
        _ => Conflict(new { message = "Unable to process this request. Refresh and try again." })
    };
}
