using WoodFlow.Application.DTOs;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public static class UserManagementRules
{
    // Called inside the repository transaction, after locking and re-reading accounts.
    public static UserWriteError Check(int actorId, ManagedUser target, ChangeUserRequest change, int activeOwners,
        bool targetIsUsableOwner = true)
    {
        if (!change.Confirmed || !Roles.IsSupported(change.Role) || !AccountStatuses.IsSupported(change.Status))
            return UserWriteError.InvalidTransition;
        if (target.Status != change.ExpectedStatus || target.Role != change.ExpectedRole || target.Version != change.ExpectedVersion)
            return UserWriteError.Conflict;
        if (target.Status == AccountStatuses.Deactivated) return UserWriteError.Deactivated;
        if (actorId == target.UserId && (change.Role != Roles.Owner || change.Status != AccountStatuses.Active))
            return UserWriteError.SelfChange;
        if (targetIsUsableOwner && target.Role == Roles.Owner && target.Status == AccountStatuses.Active && activeOwners <= 1 &&
            (change.Role != Roles.Owner || change.Status != AccountStatuses.Active))
            return UserWriteError.LastOwner;
        // One change per confirmation; no combining a role change with a status change.
        if (target.Role != change.Role && (target.Status != AccountStatuses.Active || change.Status != target.Status))
            return UserWriteError.InvalidTransition;
        if (target.Status == change.Status && target.Role == change.Role) return UserWriteError.InvalidTransition;
        return UserWriteError.None;
    }
}
