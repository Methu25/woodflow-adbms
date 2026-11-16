using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Api.Controllers;

[ApiController, Route("api/permissions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PermissionsController(PermissionService permissions, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("me"), Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await permissions.EffectiveAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), User.FindFirstValue(ClaimTypes.Role)!, ct));

    [HttpGet("check/{key}"), Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public async Task<IActionResult> Check(string key)
    {
        if (!PermissionCatalog.Keys.Contains(key)) return BadRequest(new { message = "Unknown permission." });
        var result = await authorization.AuthorizeAsync(User, HttpContext, key);
        return result.Succeeded ? NoContent() : Forbid();
    }

    [HttpGet("assistant"), Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    public async Task<IActionResult> Assistant(CancellationToken ct) => Ok(await permissions.GetMatrixAsync(ct));

    [HttpGet("users/{id:int:min(1)}"), Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    public async Task<IActionResult> UserPermissions(int id, CancellationToken ct) =>
        await permissions.GetUserAsync(id, ct) is { } settings ? Ok(settings) : NotFound(new { message = "Assistant account not found." });

    [HttpPut("users/{id:int:min(1)}"), Authorize(Policy = AuthorizationPolicies.OwnerOnly), RequestSizeLimit(4096)]
    public async Task<IActionResult> SaveUser(int id, SaveUserPermissionsRequest request, CancellationToken ct)
    {
        var result = await permissions.SaveUserAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), id, request, ct);
        return result.Error switch
        {
            PermissionSaveError.None => Ok(result.Settings),
            PermissionSaveError.Forbidden => StatusCode(403, new { message = "Owner access is required." }),
            PermissionSaveError.NotFound => NotFound(new { message = "Assistant account not found." }),
            PermissionSaveError.Deactivated => Conflict(new { message = "Deactivated accounts cannot be changed." }),
            PermissionSaveError.Conflict => Conflict(new { message = "The account or its permissions changed. Reload before saving." }),
            _ => BadRequest(new { message = "Choose valid permissions and confirm the change." })
        };
    }

    [HttpPut("assistant"), Authorize(Policy = AuthorizationPolicies.OwnerOnly), RequestSizeLimit(4096)]
    public async Task<IActionResult> Save(SavePermissionsRequest request, CancellationToken ct)
    {
        var result = await permissions.SaveAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), request, ct);
        return result.Error switch
        {
            PermissionSaveError.None => Ok(result.Settings),
            PermissionSaveError.Forbidden => StatusCode(403, new { message = "Owner access is required." }),
            PermissionSaveError.Conflict => Conflict(new { message = "Permissions changed since you opened this page. Reload before saving." }),
            _ => BadRequest(new { message = "Choose valid permissions. Create, Edit and Delete require View. Confirm your changes." })
        };
    }
}
