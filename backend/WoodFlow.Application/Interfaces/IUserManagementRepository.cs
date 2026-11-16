using WoodFlow.Application.DTOs;

namespace WoodFlow.Application.Interfaces;

public interface IUserManagementRepository
{
    Task<UserCounts> CountsAsync(CancellationToken ct);
    Task<UserPage> ListAsync(string category, string search, int page, int pageSize, CancellationToken ct);
    Task<ManagedUser?> FindAsync(int id, CancellationToken ct);
    Task<UserWriteResult> CreateAsync(int actorId, string name, string username, string hash, string role, CancellationToken ct);
    Task<UserWriteResult> ChangeAsync(int actorId, int id, ChangeUserRequest request, CancellationToken ct);
}
