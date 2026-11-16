using Microsoft.AspNetCore.Mvc;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;

namespace WoodFlow.Api.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[Route("api/health")]
public sealed class HealthController(IHealthService healthService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var health = await healthService.CheckAsync(cancellationToken);
        return StatusCode(health.DatabaseStatus == "connected" ? 200 : 503, health);
    }
}
