using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface IAuthSessionRepository
{
    Task<DateTimeOffset?> CreateAsync(User user, byte[] sessionHash, CancellationToken ct);
    Task<bool> IsActiveAsync(int userId, int authVersion, byte[] sessionHash, CancellationToken ct);
    Task RevokeAsync(int userId, byte[] sessionHash, CancellationToken ct);
}
