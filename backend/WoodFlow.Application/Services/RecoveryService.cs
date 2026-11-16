using System.Security.Cryptography;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class RecoveryService(IRecoveryRepository repository, IPasswordService passwords)
{
    public Task SubmitAsync(SubmitRecoveryRequest request, CancellationToken ct) =>
        repository.SubmitAsync(request.Username.Trim(), request.OwnerId, ct);

    public async Task<RecoveryResult<IssuedResetCode>> ApproveAsync(int actorId, Guid id,
        bool identityVerified, CancellationToken ct)
    {
        if (!identityVerified) return new(Error: RecoveryError.Conflict);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var result = await repository.ProcessAsync(actorId, id, HashCode(code), ct);
        return result.Error == RecoveryError.None
            ? new(new(code, result.Value)) : new(Error: result.Error);
    }

    public async Task<bool> CompleteAsync(CompleteRecoveryRequest request, CancellationToken ct)
    {
        if (request.NewPassword.Length is < 8 or > 128 || string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword != request.ConfirmPassword || request.Code.Length != 64 ||
            !request.Code.All(Uri.IsHexDigit)) return false;
        // PasswordHasher does not use account fields. Never persist or log this temporary entity.
        var hash = passwords.Hash(new User(0, "", "", "", Roles.Assistant, true), request.NewPassword);
        return await repository.CompleteAsync(HashCode(request.Code), hash, ct);
    }

    public static byte[] HashCode(string code) => SHA256.HashData(Convert.FromHexString(code));
}
