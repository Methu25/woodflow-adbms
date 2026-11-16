using WoodFlow.Application.DTOs;
using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface IAuthenticationService
{
    Task<User?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<User?> RevalidateAsync(int userId, CancellationToken cancellationToken);
}
