using System.ComponentModel.DataAnnotations;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class UserManagementService(IUserManagementRepository repository, IPasswordService passwords)
{
    public Task<UserWriteResult> CreateAsync(int actorId, CreateUserRequest request, CancellationToken ct)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true) ||
            string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Password) || !Roles.IsSupported(request.Role))
            return Task.FromResult(new UserWriteResult(Error: UserWriteError.InvalidTransition));
        var user = new User(0, request.Username, "", request.FullName.Trim(), request.Role, true);
        return repository.CreateAsync(actorId, user.FullName, user.Username, passwords.Hash(user, request.Password), user.Role, ct);
    }

    public Task<UserWriteResult> ChangeAsync(int actorId, int id, ChangeUserRequest request, CancellationToken ct)
    {
        if (id <= 0 || !request.Confirmed || !Roles.IsSupported(request.Role) || !Roles.IsSupported(request.ExpectedRole) ||
            !AccountStatuses.IsSupported(request.Status) || !AccountStatuses.IsSupported(request.ExpectedStatus) || request.ExpectedVersion < 0)
            return Task.FromResult(new UserWriteResult(Error: UserWriteError.InvalidTransition));
        return repository.ChangeAsync(actorId, id, request, ct);
    }
}
