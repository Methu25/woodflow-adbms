using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class PermissionService(IPermissionRepository repository)
{
    public static bool Valid(string[]? keys) => keys is not null && keys.Length <= PermissionCatalog.Keys.Count &&
        keys.Distinct(StringComparer.Ordinal).Count() == keys.Length && keys.All(k => k is not null && PermissionCatalog.Keys.Contains(k)) &&
        PermissionCatalog.Modules.All(m => !m.Permissions.Any(p => p.Action != "view" && keys.Contains(p.Key)) || keys.Contains($"{m.Key}.view"));

    public async Task<PermissionMatrix> GetMatrixAsync(CancellationToken ct)
    {
        var settings = await repository.GetAsync(ct);
        return new(PermissionCatalog.Modules, settings.GrantedKeys, settings.Version);
    }
    public async Task<EffectivePermissions> EffectiveAsync(int userId, string role, CancellationToken ct)
    {
        if (role == Roles.Owner) return new(role, PermissionCatalog.Keys.Order(StringComparer.Ordinal).ToArray());
        if (role != Roles.Assistant) return new(role, []);
        var settings = await repository.GetEffectiveAsync(userId, ct);
        if (!Valid(settings.GrantedKeys)) throw new InvalidOperationException("Permission configuration is invalid.");
        return new(role, settings.GrantedKeys);
    }
    public Task<PermissionSaveResult> SaveAsync(int actorId, SavePermissionsRequest request, CancellationToken ct)
    {
        if (!request.Confirmed || request.ExpectedVersion < 0 || !Valid(request.GrantedKeys))
            return Task.FromResult(new PermissionSaveResult(Error: PermissionSaveError.Invalid));
        return repository.SaveAsync(actorId, request.GrantedKeys.Order(StringComparer.Ordinal).ToArray(), request.ExpectedVersion, ct);
    }

    public async Task<UserPermissionMatrix?> GetUserAsync(int userId, CancellationToken ct)
    {
        var settings = await repository.GetUserAsync(userId, ct);
        return settings is null ? null : new(PermissionCatalog.Modules, settings.User, settings.GrantedKeys,
            settings.InheritsRole, settings.Version, settings.RoleVersion);
    }
    public Task<UserPermissionSaveResult> SaveUserAsync(int actorId, int userId, SaveUserPermissionsRequest request, CancellationToken ct)
    {
        if (userId <= 0 || !request.Confirmed || request.ExpectedVersion < 0 || request.ExpectedRoleVersion < 0 ||
            request.ExpectedAccountVersion < 0 || !Valid(request.GrantedKeys) || (request.UseRoleDefaults && request.GrantedKeys.Length != 0))
            return Task.FromResult(new UserPermissionSaveResult(Error: PermissionSaveError.Invalid));
        return repository.SaveUserAsync(actorId, userId, request, ct);
    }
}
