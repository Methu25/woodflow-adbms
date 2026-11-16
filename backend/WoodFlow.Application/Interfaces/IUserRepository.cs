using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken);
    Task UpdateHashAsync(int userId, string oldHash, string newHash, CancellationToken cancellationToken);
}
