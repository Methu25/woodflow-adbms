using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using WoodFlow.Application.Services;
using WoodFlow.Domain;

namespace WoodFlow.Api.Security;

public sealed record PermissionRequirement(string Key) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(PermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !PermissionCatalog.Keys.Contains(requirement.Key)) return;
        if (context.User.IsInRole(Roles.Owner)) { context.Succeed(requirement); return; }
        if (!context.User.IsInRole(Roles.Assistant)) return;
        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        // Read current PostgreSQL settings on every protected request; no stale cookie grants/cache.
        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        var effective = await permissions.EffectiveAsync(userId, Roles.Assistant, ct);
        if (effective.GrantedKeys.Contains(requirement.Key)) context.Succeed(requirement);
    }
}
