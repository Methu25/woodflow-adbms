using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;

namespace WoodFlow.Application.Services;

public sealed class SetupService(ISetupRepository repository, IPasswordService passwords) : ISetupService
{
    public async Task<string?> GenerateCodeAsync(CancellationToken cancellationToken)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return await repository.IssueCodeAsync(HashCode(code), cancellationToken) ? code : null;
    }

    public async Task<bool> InitializeOwnerAsync(InitializeOwnerRequest request, CancellationToken cancellationToken)
    {
        if (!Validator.TryValidateObject(request, new ValidationContext(request), [], true)
            || string.IsNullOrWhiteSpace(request.FullName)
            || request.SetupCode.Any(c => !Uri.IsHexDigit(c)))
            return false;

        var user = new User(0, request.Username, "", request.FullName.Trim(), Roles.Owner, true);
        var hash = passwords.Hash(user, request.Password);
        return await repository.InitializeOwnerAsync(user.FullName, user.Username, hash,
            HashCode(request.SetupCode.ToUpperInvariant()), cancellationToken);
    }

    private static byte[] HashCode(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
