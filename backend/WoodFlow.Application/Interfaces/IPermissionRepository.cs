using WoodFlow.Application.DTOs;

namespace WoodFlow.Application.Interfaces;

public interface IPermissionRepository
{
    Task<PermissionSettings> GetAsync(CancellationToken ct);
    Task<PermissionSaveResult> SaveAsync(int actorId, string[] grantedKeys, long expectedVersion, CancellationToken ct);
    Task<PermissionSettings> GetEffectiveAsync(int userId, CancellationToken ct);
    Task<UserPermissionSettings?> GetUserAsync(int userId, CancellationToken ct);
    Task<UserPermissionSaveResult> SaveUserAsync(int actorId, int userId, SaveUserPermissionsRequest request, CancellationToken ct);
}
