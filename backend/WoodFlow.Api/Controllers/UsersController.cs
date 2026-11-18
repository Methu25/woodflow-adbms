using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Controllers;

[ApiController, Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(IUserManagementRepository repository, UserManagementService service) : ControllerBase
{
    [HttpGet("counts")]
    public async Task<ActionResult<UserCounts>> Counts(CancellationToken ct) => Ok(await repository.CountsAsync(ct));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, string category = "owners", string? search = null, int page = 1, int pageSize = 10)
    {
        if (category is not ("owners" or "assistants" or "suspended" or "deactivated") || (search?.Length ?? 0) > 100 ||
            page < 1 || page > 100000 || pageSize < 1 || pageSize > 50) return BadRequest(new { message = "Invalid search or page." });
        return Ok(await repository.ListAsync(category, (search ?? "").Trim(), page, pageSize, ct));
    }

    [HttpGet("{id:int:min(1)}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        await repository.FindAsync(id, ct) is { } user ? Ok(user) : NotFound(new { message = "Account not found." });

    [HttpPost, RequestSizeLimit(4096)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct) =>
        Result(await service.CreateAsync(ActorId, request, ct), true);

    [HttpPatch("{id:int:min(1)}"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Change(int id, ChangeUserRequest request, CancellationToken ct) =>
        Result(await service.ChangeAsync(ActorId, id, request, ct));

    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IActionResult Result(UserWriteResult result, bool created = false) => result.Error switch
    {
        UserWriteError.None => created ? CreatedAtAction(nameof(Details), new { id = result.User!.UserId }, result.User) : Ok(result.User),
        UserWriteError.Forbidden => StatusCode(403, new { message = "Owner access is required." }),
        UserWriteError.NotFound => NotFound(new { message = "Account not found." }),
        UserWriteError.DuplicateUsername => Conflict(new { message = "That username is already in use." }),
        UserWriteError.Conflict => Conflict(new { message = "This account changed. Refresh and try again." }),
        UserWriteError.LastOwner => Conflict(new { message = "The last active OWNER must be retained." }),
        UserWriteError.SelfChange => Conflict(new { message = "You cannot suspend, deactivate, or demote your own account." }),
        UserWriteError.Deactivated => Conflict(new { message = "Deactivation is permanent. This account cannot be changed." }),
        _ => BadRequest(new { message = "Confirm a valid account change." })
    };
}
