using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class AuthenticationService(IUserRepository users, IPasswordService passwords) : IAuthenticationService
{
    // A real dummy hash makes unknown/inactive/placeholder accounts perform a password KDF too.
    private readonly User _dummy = new(0, "", passwords.Hash(
        new User(0, "", "", "", Roles.Assistant, false), Guid.NewGuid().ToString()), "", Roles.Assistant, false);

    public async Task<User?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByUsernameAsync(request.Username, cancellationToken);
        var eligible = user is { IsActive: true } && Roles.IsSupported(user.Role)
            && passwords.IsUsableHash(user.PasswordHash);
        var valid = passwords.Verify(eligible ? user! : _dummy, request.Password, out var rehash);
        if (!eligible || !valid)
            return null;

        if (rehash)
        {
            var hash = passwords.Hash(user!, request.Password);
            await users.UpdateHashAsync(user!.UserId, user.PasswordHash, hash, cancellationToken);
            user = user with { PasswordHash = hash };
        }
        return user;
    }

    public async Task<User?> RevalidateAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is { IsActive: true } && Roles.IsSupported(user.Role)
            && passwords.IsUsableHash(user.PasswordHash) ? user : null;
    }

    public static AccountResponse ToResponse(User user) =>
        new(user.UserId, user.Username, user.FullName, user.Role);
}
