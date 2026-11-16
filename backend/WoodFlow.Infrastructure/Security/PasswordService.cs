using System.Buffers.Binary;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Infrastructure.Security;

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new(Options.Create(
        new PasswordHasherOptions { IterationCount = 210_000 }));

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    // Reject seed placeholders and malformed/unsupported hashes before PasswordHasher.
    public bool IsUsableHash(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length > 255)
            return false;
        Span<byte> bytes = stackalloc byte[255];
        if (!Convert.TryFromBase64String(hash, bytes, out var count))
            return false;
        if (count == 49 && bytes[0] == 0) // Identity V2
            return true;
        if (count < 45 || bytes[0] != 1)
            return false;
        var prf = BinaryPrimitives.ReadUInt32BigEndian(bytes[1..5]);
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes[5..9]);
        var saltLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[9..13]);
        return prf <= 2 && iterations is > 0 and <= 1_000_000
            && saltLength >= 16 && saltLength <= count - 29;
    }

    public bool Verify(User user, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (!IsUsableHash(user.PasswordHash))
            return false;
        try
        {
            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
