using System.ComponentModel.DataAnnotations;
using WoodFlow.Domain;

namespace WoodFlow.Application.DTOs;

public sealed record PermissionSettings(string[] GrantedKeys, long Version);
public sealed record PermissionMatrix(IReadOnlyList<PermissionModule> Modules, string[] GrantedKeys, long Version);
public sealed record EffectivePermissions(string Role, string[] GrantedKeys);
public sealed class SavePermissionsRequest
{
    [Required, MaxLength(32)] public string[] GrantedKeys { get; init; } = [];
    [Range(0, long.MaxValue)] public long ExpectedVersion { get; init; }
    public bool Confirmed { get; init; }
}
public enum PermissionSaveError { None, Invalid, Conflict, Forbidden, NotFound, Deactivated }
public sealed record PermissionSaveResult(PermissionSettings? Settings = null, PermissionSaveError Error = PermissionSaveError.None);

public sealed record UserPermissionSettings(ManagedUser User, string[] GrantedKeys, bool InheritsRole, long Version, long RoleVersion);
public sealed record UserPermissionMatrix(IReadOnlyList<PermissionModule> Modules, ManagedUser User,
    string[] GrantedKeys, bool InheritsRole, long Version, long RoleVersion);
public sealed class SaveUserPermissionsRequest
{
    [Required, MaxLength(32)] public string[] GrantedKeys { get; init; } = [];
    [Range(0, long.MaxValue)] public long ExpectedVersion { get; init; }
    [Range(0, long.MaxValue)] public long ExpectedRoleVersion { get; init; }
    [Range(0, int.MaxValue)] public int ExpectedAccountVersion { get; init; }
    public bool UseRoleDefaults { get; init; }
    public bool Confirmed { get; init; }
}
public sealed record UserPermissionSaveResult(UserPermissionSettings? Settings = null, PermissionSaveError Error = PermissionSaveError.None);
