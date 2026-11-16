namespace WoodFlow.Application.Interfaces;

public interface IHealthRepository
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
