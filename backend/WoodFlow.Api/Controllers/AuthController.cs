using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Globalization;
using WoodFlow.Api.Security;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Services;
using WoodFlow.Domain;
using WoodFlow.Application.Interfaces;
using AuthenticationService = WoodFlow.Application.Services.AuthenticationService;
using IAuthenticationService = WoodFlow.Application.Interfaces.IAuthenticationService;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IAuthenticationService authentication, IAntiforgery antiforgery,
    AccountSessionService sessions, AccountLoginThrottle throttle) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
            new CookieOptions { HttpOnly = false, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
        return NoContent();
    }

    [HttpPost("login")]
    [RequestSizeLimit(4096)]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var retryAfter = throttle.RetryAfterSeconds(request.Username);
        if (retryAfter > 0)
        {
            Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            return StatusCode(429, new { message = "Too many sign-in attempts. Try again shortly." });
        }
        var user = await authentication.LoginAsync(request, cancellationToken);
        if (user is null)
            return Unauthorized(new { message = "Unable to sign in." });
        var session = await sessions.StartAsync(user, cancellationToken);
        if (session is null) return Unauthorized(new { message = "Unable to sign in." });
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AccountPrincipal.Create(user, session.Handle),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = session.ExpiresAt });
        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Secure = true, Path = "/" });
        return Ok(AuthenticationService.ToResponse(user));
    }

    [HttpPost("logout")]
    [Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Commit revocation before reporting success or removing the browser cookie. Fail closed on DB errors.
        await sessions.RevokeAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            User.FindFirstValue(AccountPrincipal.SessionHandle), ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Secure = true, Path = "/" });
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public ActionResult<AccountResponse> Me() =>
        Ok(AuthenticationService.ToResponse((User)HttpContext.Items[typeof(User)]!));
}
