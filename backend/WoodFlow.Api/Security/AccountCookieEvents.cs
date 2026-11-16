using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using WoodFlow.Application.Interfaces;
using IAuthenticationService = WoodFlow.Application.Interfaces.IAuthenticationService;
using WoodFlow.Application.Services;

namespace WoodFlow.Api.Security;

public sealed class AccountCookieEvents(IAuthenticationService authentication, AccountSessionService sessions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = int.TryParse(id, out var userId)
            ? await authentication.RevalidateAsync(userId, context.HttpContext.RequestAborted) : null;
        var handle = context.Principal?.FindFirstValue(AccountPrincipal.SessionHandle);
        if (user is null || context.Principal?.FindFirstValue(AccountPrincipal.CredentialStamp) != AccountPrincipal.Stamp(user)
            || !await sessions.IsActiveAsync(user, handle, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        // Authorization always uses the current database role, never a stale cookie role.
        context.ReplacePrincipal(AccountPrincipal.Create(user, handle!));
        context.HttpContext.Items[typeof(WoodFlow.Domain.User)] = user;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
