using System.Security.Cryptography;
using System.Text;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Domain;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

public sealed class PasswordAndSetupTests
{
    [Theory]
    [InlineData("OWNER", true)]
    [InlineData("ASSISTANT", true)]
    [InlineData("CASHIER", false)]
    [InlineData("WORKSHOP_SUPERVISOR", false)]
    public void OnlyFinalRolesAreSupported(string role, bool expected) =>
        Assert.Equal(expected, Roles.IsSupported(role));

    [Theory]
    [InlineData("hash_owner_123")]
    [InlineData("hash_cashier_123")]
    [InlineData("hash_foreman_123")]
    [InlineData("")]
    [InlineData("AAAA")]
    public void PlaceholderAndMalformedHashesFail(string hash)
    {
        var service = new PasswordService();
        Assert.False(service.IsUsableHash(hash));
        Assert.False(service.Verify(new User(1, "test", hash, "Test", Roles.Owner, true), "password", out _));
    }

    [Fact]
    public void HasherRoundTripAndWrongPassword()
    {
        var passwords = new PasswordService();
        var user = new User(1, "test", "", "Test", Roles.Owner, true);
        user = user with { PasswordHash = passwords.Hash(user, "Example secure password") };
        Assert.True(passwords.IsUsableHash(user.PasswordHash));
        Assert.True(passwords.Verify(user, "Example secure password", out _));
        Assert.False(passwords.Verify(user, "wrong password", out _));
        Assert.NotEqual("Example secure password", user.PasswordHash);
        Assert.DoesNotContain(user.PasswordHash, user.ToString());
    }

    [Fact]
    public async Task CodeIsRandomHashedBeforePersistenceAndNeverReturnedBySetup()
    {
        var repository = new CapturingSetup();
        var service = new SetupService(repository, new PasswordService());
        var first = await service.GenerateCodeAsync(default);
        var second = await service.GenerateCodeAsync(default);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, second);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(second)), repository.CodeHash);
        var request = new InitializeOwnerRequest
        {
            FullName = "New Owner", Username = "new-owner", Password = "Example secure password",
            ConfirmPassword = "Example secure password", SetupCode = second
        };
        Assert.True(await service.InitializeOwnerAsync(request, default));
        Assert.True(new PasswordService().IsUsableHash(repository.PasswordHash!));
    }

    [Fact]
    public async Task WeakPasswordMismatchedConfirmationAndBadCodesNeverReachRepository()
    {
        var repository = new CapturingSetup();
        var service = new SetupService(repository, new PasswordService());
        foreach (var request in new[]
        {
            new InitializeOwnerRequest { FullName = "Owner", Username = "owner", Password = "short", ConfirmPassword = "short", SetupCode = new string('A', 64) },
            new InitializeOwnerRequest { FullName = "Owner", Username = "owner", Password = "Example secure password", ConfirmPassword = "different", SetupCode = new string('A', 64) },
            new InitializeOwnerRequest { FullName = "Owner", Username = "owner", Password = "Example secure password", ConfirmPassword = "Example secure password", SetupCode = new string('Z', 64) }
        })
            Assert.False(await service.InitializeOwnerAsync(request, default));
        Assert.Null(repository.PasswordHash);
    }

    private sealed class CapturingSetup : ISetupRepository
    {
        public byte[]? CodeHash { get; private set; }
        public string? PasswordHash { get; private set; }
        public Task<bool> IssueCodeAsync(byte[] hash, CancellationToken ct)
        {
            CodeHash = hash;
            return Task.FromResult(true);
        }
        public Task<bool> InitializeOwnerAsync(string name, string username, string password, byte[] hash,
            CancellationToken ct)
        {
            PasswordHash = password;
            Assert.Equal(CodeHash, hash);
            return Task.FromResult(true);
        }
    }
}
