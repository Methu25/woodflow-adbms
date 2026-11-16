using WoodFlow.Application.DTOs;

namespace WoodFlow.Application.Interfaces;

public interface IHealthService
{
    Task<HealthResponse> CheckAsync(CancellationToken cancellationToken);
}
