using System.Security.Cryptography;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed record AccountSession(string Handle, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(AccountSession);
}

public sealed class AccountSessionService(IAuthSessionRepository repository)
{
    public async Task<AccountSession?> StartAsync(User user, CancellationToken ct)
    {
        var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = await repository.CreateAsync(user, Hash(handle), ct);
        return expires is { } value ? new(handle, value) : null;
    }
    public Task<bool> IsActiveAsync(User user, string? handle, CancellationToken ct) => Valid(handle)
        ? repository.IsActiveAsync(user.UserId, user.AuthVersion, Hash(handle!), ct) : Task.FromResult(false);
    public Task RevokeAsync(int userId, string? handle, CancellationToken ct) => Valid(handle)
        ? repository.RevokeAsync(userId, Hash(handle!), ct) : Task.CompletedTask;
    private static bool Valid(string? handle) => handle is { Length: 64 } && handle.All(Uri.IsHexDigit);
    private static byte[] Hash(string handle) => SHA256.HashData(Convert.FromHexString(handle));
}
