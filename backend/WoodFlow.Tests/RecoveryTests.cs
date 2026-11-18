using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoodFlow.Application.DTOs;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Domain;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

// Real HTTP middleware/cookies/CSRF and service; repository is a recording test double. No local DB writes.
public sealed class RecoveryTests
{
    private const string Password = "Recovery fixture password 42!";
    private static readonly Guid RequestId = Guid.NewGuid();
    private static async Task<string> Csrf(HttpClient client)
    {
        var r = await client.GetAsync("/api/auth/csrf"); r.EnsureSuccessStatusCode();
        return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="))
            .Split(';')[0]["XSRF-TOKEN=".Length..]);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }
    private static async Task Login(HttpClient c, string username) => Assert.Equal(HttpStatusCode.OK,
        (await Post(c, "/api/auth/login", new { username, password = Password })).StatusCode);

    [Fact]
    public async Task RecoveryAdministrationRequiresOwnerAndReturnsNoCodeInReads()
    {
        await using var f = new Factory(); using var c = f.Client();
        foreach (var path in new[] { "requests", "pending-count", $"requests/{RequestId}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/password-recovery/" + path)).StatusCode);
        await Login(c, "assistant");
        foreach (var path in new[] { "requests", "pending-count", $"requests/{RequestId}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/password-recovery/" + path)).StatusCode);
        foreach (var action in new[] { "approve", "deny" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/api/password-recovery/requests/{RequestId}/{action}",
                new { identityVerified = true })).StatusCode);
        using var owner = f.Client(); await Login(owner, "owner");
        var response = await owner.GetStringAsync("/api/password-recovery/requests");
        Assert.DoesNotContain("hash", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code", response, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/password-recovery/pending-count")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/password-recovery/requests/{RequestId}")).StatusCode);
    }

    [Fact]
    public async Task ApprovalNeedsIdentityConfirmationAndRepositoryAuthorization()
    {
        await using var f = new Factory(); using var owner = f.Client(); await Login(owner, "owner");
        var path = $"/api/password-recovery/requests/{RequestId}/approve";
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, path, new { identityVerified = false })).StatusCode);
        Assert.Null(f.Recovery.LastCodeHash);
        using var other = f.Client(); await Login(other, "other-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, path, new { identityVerified = true })).StatusCode);
        var response = await Post(owner, path, new { identityVerified = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var issued = (await response.Content.ReadFromJsonAsync<IssuedResetCode>())!;
        Assert.Equal(64, issued.Code.Length);
        Assert.Equal(RecoveryService.HashCode(issued.Code), f.Recovery.LastCodeHash);
        Assert.Equal(nameof(IssuedResetCode), issued.ToString());
    }


    [Fact]
    public async Task PublicResponsesAreGenericAndContactsAreMinimal()
    {
        await using var f = new Factory(); using var c = f.Client();
        string? expected = null;
        foreach (var username in new[] { "assistant", "owner", "missing", "inactive", "placeholder" })
        {
            var r = await Post(c, "/api/password-recovery/requests", new { username, ownerId = 1 });
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync(); expected ??= body; Assert.Equal(expected, body);
            Assert.DoesNotContain("requestId", body);
        }
        var contacts = await c.GetStringAsync("/api/password-recovery/owners");
        Assert.Equal("[{\"ownerId\":1,\"fullName\":\"Owner contact\"}]", contacts);
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("complete")]
    [InlineData("requests/00000000-0000-0000-0000-000000000001/approve")]
    [InlineData("requests/00000000-0000-0000-0000-000000000001/deny")]
    public async Task EveryStateChangeRequiresCsrf(string suffix)
    {
        await using var f = new Factory(); using var c = f.Client();
        if (suffix.Contains('/')) await Login(c, "owner");
        var body = new { username = "assistant", ownerId = 1, code = new string('A', 64),
            newPassword = Password, confirmPassword = Password, identityVerified = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/password-recovery/" + suffix, body)).StatusCode);
        Assert.Null(f.Recovery.LastCodeHash);
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("complete")]
    public async Task PublicWritesAreRateLimitedAndHttpIsRejected(string suffix)
    {
        await using var f = new Factory(); using var c = f.Client();
        var body = new { username = "missing", ownerId = 1, code = new string('A', 64),
            newPassword = Password, confirmPassword = Password };
        for (var i = 0; i < 10; i++) await Post(c, "/api/password-recovery/" + suffix, body);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(c, "/api/password-recovery/" + suffix, body)).StatusCode);
        using var http = f.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/password-recovery/" + suffix, body)).StatusCode);
    }

    [Fact]
    public async Task CompletionHashesPasswordAndInvalidatesExistingCookie()
    {
        await using var f = new Factory(); using var assistant = f.Client(); await Login(assistant, "assistant");
        using var anonymous = f.Client();
        f.Recovery.AcceptCompletion = true;
        var response = await Post(anonymous, "/api/password-recovery/complete", new
        { code = new string('A', 64), newPassword = "New test password 73!", confirmPassword = "New test password 73!" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = f.Users.Users[2];
        Assert.Equal(1, user.AuthVersion);
        Assert.True(new PasswordService().Verify(user, "New test password 73!", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await assistant.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ServiceCreatesIndependentHighEntropyCodesAndRejectsBadPasswordInputs()
    {
        var repository = new RecordingRecovery(new(Password));
        var service = new RecoveryService(repository, new PasswordService());
        var a = (await service.ApproveAsync(1, RequestId, true, default)).Value!;
        var first = repository.LastCodeHash!;
        var b = (await service.ApproveAsync(1, RequestId, true, default)).Value!;
        Assert.NotEqual(a.Code, b.Code);
        Assert.Equal(32, first.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(first, RecoveryService.HashCode(a.Code)));
        Assert.False(await service.CompleteAsync(new() { Code = a.Code, NewPassword = "short", ConfirmPassword = "short" }, default));
        Assert.False(await service.CompleteAsync(new() { Code = a.Code, NewPassword = Password, ConfirmPassword = "different" }, default));
        Assert.False(await service.CompleteAsync(new() { Code = "invalid", NewPassword = Password, ConfirmPassword = Password }, default));
        Assert.False(await service.CompleteAsync(new() { Code = a.Code, NewPassword = "        ", ConfirmPassword = "        " }, default));
    }

    [Theory]
    [InlineData("pending", "PENDING")]
    [InlineData("completed", "APPROVED,COMPLETED,EXPIRED")]
    [InlineData("denied", "DENIED")]
    public async Task OwnerWorkCategoriesPreserveSecurityLifecycle(string category, string expected)
    {
        await using var f = new Factory(); using var owner = f.Client(); await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/password-recovery/requests?category=" + category)).StatusCode);
        Assert.Equal(expected, f.Recovery.LastInboxStatus);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/password-recovery/requests?category=invalid")).StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public AuthApiTests.MemoryUsers Users { get; } = new(Password);
        public RecordingRecovery Recovery { get; }
        public Factory()
        {
            var user = Users.Users[1] with { UserId = 6, Username = "other-owner" };
            Users.Users[6] = user; Recovery = new(Users);
        }
        public HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IUserRepository>(); services.AddSingleton<IUserRepository>(Users);
            services.RemoveAll<IAuthSessionRepository>(); services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
            services.RemoveAll<IRecoveryRepository>(); services.AddSingleton<IRecoveryRepository>(Recovery);
        });
    }

    [Fact]
    public async Task ResolutionRequiresOwnerCsrfIdentityAndConfirmation()
    {
        await using var f = new Factory(); using var anonymous = f.Client();
        var body = new { username = "assistant", resolution = "REASSIGN_TO_ME", identityVerified = true, confirmed = true };
        const string path = "/api/password-recovery/resolve-pending";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, path, body)).StatusCode);
        using var assistant = f.Client(); await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(assistant, path, body)).StatusCode);
        using var owner = f.Client(); await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, path,
            new { username = "assistant", resolution = "REASSIGN_TO_ME", identityVerified = false, confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, path,
            new { username = "assistant", resolution = "REASSIGN_TO_ME", identityVerified = true, confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, path,
            new { username = "owner", resolution = "REASSIGN_TO_ME", identityVerified = true, confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(owner, path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(owner, path,
            new { username = "assistant", resolution = "DENY", identityVerified = true, confirmed = true })).StatusCode);
    }

    private sealed class RecordingRecovery(AuthApiTests.MemoryUsers users) : IRecoveryRepository
    {
        public byte[]? LastCodeHash { get; private set; }
        public bool AcceptCompletion { get; set; }
        public string? LastInboxStatus { get; private set; }
        public Task<IReadOnlyList<RecoveryOwnerContact>> OwnersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RecoveryOwnerContact>>([new(1, "Owner contact")]);
        public Task SubmitAsync(string username, int ownerId, CancellationToken ct) => Task.CompletedTask;
        public Task<RecoveryError> ResolvePendingAsync(int actorId, string username, bool deny, CancellationToken ct) =>
            Task.FromResult(actorId == 1 && username != "owner" ? RecoveryError.None : RecoveryError.Conflict);
        public Task<RecoveryResult<RecoveryPage>> InboxAsync(int actorId, string? status, int page, int size, CancellationToken ct)
        {
            LastInboxStatus = status;
            return Task.FromResult(new RecoveryResult<RecoveryPage>(new([], 0, page, size)));
        }
        public Task<RecoveryResult<int>> PendingCountAsync(int actorId, CancellationToken ct) => Task.FromResult(new RecoveryResult<int>(0));
        public Task<RecoveryResult<RecoveryItem>> DetailsAsync(int actorId, Guid id, CancellationToken ct) =>
            Task.FromResult(new RecoveryResult<RecoveryItem>(new(id, 2, "Assistant", "assistant", Roles.Assistant, "PENDING",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, null, null)));
        public Task<RecoveryResult<DateTimeOffset>> ProcessAsync(int actorId, Guid id, byte[]? hash, CancellationToken ct)
        {
            if (actorId != 1) return Task.FromResult(new RecoveryResult<DateTimeOffset>(Error: RecoveryError.NotFound));
            LastCodeHash = hash;
            return Task.FromResult(new RecoveryResult<DateTimeOffset>(DateTimeOffset.UtcNow.AddMinutes(10)));
        }
        public Task<bool> CompleteAsync(byte[] hash, string password, CancellationToken ct)
        {
            if (AcceptCompletion)
                users.Users[2] = users.Users[2] with { PasswordHash = password, AuthVersion = users.Users[2].AuthVersion + 1 };
            return Task.FromResult(AcceptCompletion);
        }
    }
}
