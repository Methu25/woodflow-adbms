using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Route("api/setup")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SetupController(ISetupService setup) : ControllerBase
{
    [HttpPost("initialize-owner")]
    [RequestSizeLimit(4096)]
    [AllowAnonymous]
    [EnableRateLimiting("setup")]
    public async Task<IActionResult> InitializeOwner(InitializeOwnerRequest request, CancellationToken cancellationToken)
    {
        return await setup.InitializeOwnerAsync(request, cancellationToken)
            ? NoContent()
            : BadRequest(new { message = "Unable to initialize account." });
    }
}
