using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;

namespace WoodFlow.Application.Services;

public sealed class HealthService(IHealthRepository repository) : IHealthService
{
    public async Task<HealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var connected = await repository.CanConnectAsync(cancellationToken);
        return new HealthResponse("running", connected ? "connected" : "unavailable");
    }
}
