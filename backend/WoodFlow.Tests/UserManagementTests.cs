using System.Net;
using System.Net.Http.Json;
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

// Real controller, cookie/CSRF middleware, validation and PasswordHasher; no local DB access.
public sealed class UserManagementTests
{
    private const string Password = "Mixed-test password 42!";
    private static ChangeUserRequest Change(ManagedUser user, string status, string? role = null, bool confirmed = true) => new()
    {
        ExpectedRole = user.Role, ExpectedStatus = user.Status, ExpectedVersion = user.Version,
        Role = role ?? user.Role, Status = status, Confirmed = confirmed
    };

    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var value = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(value.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-XSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }
    private static async Task Login(HttpClient client, string username)
    {
        var response = await Send(client, HttpMethod.Post, "/api/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/users/counts")]
    [InlineData("/api/users/1")]
    public async Task OwnerOnlyReadEndpoints(string path)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        await Login(client, "assistant");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        using var owner = factory.Client();
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task CreationValidationDuplicateAndSafeResponse()
    {
        await using var factory = new Factory();
        using var owner = factory.Client();
        await Login(owner, "owner");
        var body = new { fullName = "New Assistant", username = "new.assistant", password = Password, role = Roles.Assistant };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Post, "/api/users", body, false)).StatusCode);
        var response = await Send(owner, HttpMethod.Post, "/api/users", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, text);
        var created = (await response.Content.ReadFromJsonAsync<ManagedUser>())!;
        var stored = factory.Users.Users[created.UserId];
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(new PasswordService().Verify(stored, Password, out _));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Post, "/api/users", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Post, "/api/users",
            new { fullName = "Duplicate", username = "NEW.ASSISTANT", password = Password, role = Roles.Owner })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Post, "/api/users",
            new { fullName = " ", username = "bad name", password = "short", role = "ADMIN" })).StatusCode);
        var page = (await owner.GetFromJsonAsync<UserPage>("/api/users?category=assistants&search=new.assistant&pageSize=1"))!;
        Assert.Equal(1, page.Total); Assert.Single(page.Items);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/users?category=owners&search=&page=1&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/users?pageSize=1000")).StatusCode);
        using var assistant = factory.Client(); await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(assistant, HttpMethod.Post, "/api/users", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(assistant, HttpMethod.Patch, "/api/users/1", Change(factory.Managed.Get(1), "SUSPENDED"))).StatusCode);
    }

    [Theory]
    [InlineData("4826")]
    [InlineData("1234567")]
    [InlineData("        ")]
    public async Task NewUserPasswordRejectsShortOrWhitespaceValues(string password)
    {
        await using var factory = new Factory();
        using var owner = factory.Client();
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Post, "/api/users",
            new { fullName = "Test User", username = "test.new", password, role = Roles.Assistant })).StatusCode);
    }

    [Theory]
    [InlineData("Ab12!xyz")]
    [InlineData("48261937")]
    [InlineData("A longer mixed password 42!")]
    public async Task NewUserPasswordAllowsEightOrMoreMixedCharacters(string password)
    {
        await using var factory = new Factory();
        using var owner = factory.Client();
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.Created, (await Send(owner, HttpMethod.Post, "/api/users",
            new { fullName = "Test User", username = "test.mixed", password, role = Roles.Assistant })).StatusCode);
    }

    [Fact]
    public async Task NewUserPasswordRejectsMoreThan128Characters()
    {
        await using var factory = new Factory();
        using var owner = factory.Client();
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Post, "/api/users",
            new { fullName = "Test User", username = "test.long", password = new string('x', 129), role = Roles.Assistant })).StatusCode);
    }

    [Fact]
    public async Task StatusChangesRevokeSessionsAndDeactivationIsPermanent()
    {
        await using var factory = new Factory();
        using var owner = factory.Client(); using var assistant = factory.Client();
        await Login(owner, "owner"); await Login(assistant, "assistant");
        var original = factory.Managed.Get(2);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(original, "SUSPENDED"), false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(original, "SUSPENDED", confirmed: false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(original, "SUSPENDED"))).StatusCode);
        var suspended = factory.Managed.Get(2);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(original, "DEACTIVATED"))).StatusCode);
        using var denied = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(denied, HttpMethod.Post, "/api/auth/login", new { username = "assistant", password = Password })).StatusCode);
        // Reactivate before that browser presents its old cookie: it must still be invalid.
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(suspended, "ACTIVE"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await assistant.GetAsync("/api/auth/me")).StatusCode);
        await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(factory.Managed.Get(2), "DEACTIVATED"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await assistant.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(factory.Managed.Get(2), "ACTIVE"))).StatusCode);
        Assert.True(factory.Users.Users.ContainsKey(2));
    }

    [Fact]
    public async Task SelfChangesAreBlockedAndRolesAreRevalidated()
    {
        await using var factory = new Factory();
        using var owner = factory.Client(); await Login(owner, "owner");
        foreach (var change in new[] { Change(factory.Managed.Get(1), "SUSPENDED"), Change(factory.Managed.Get(1), "DEACTIVATED"), Change(factory.Managed.Get(1), "ACTIVE", Roles.Assistant) })
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Patch, "/api/users/1", change)).StatusCode);
        using var assistant = factory.Client(); await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Patch, "/api/users/2", Change(factory.Managed.Get(2), "ACTIVE", Roles.Owner))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await assistant.GetAsync("/api/users")).StatusCode);
        await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.OK, (await assistant.GetAsync("/api/users")).StatusCode);
    }

    [Theory]
    [InlineData("SUSPENDED", "OWNER")]
    [InlineData("DEACTIVATED", "OWNER")]
    [InlineData("ACTIVE", "ASSISTANT")]
    public void LastOwnerInvariant(string status, string role)
    {
        var target = new ManagedUser(1, "Owner", "owner", Roles.Owner, "ACTIVE", null, 0);
        Assert.Equal(UserWriteError.LastOwner, UserManagementRules.Check(99, target, Change(target, status, role), 1));
        Assert.Equal(UserWriteError.None, UserManagementRules.Check(99, target, Change(target, status, role), 2));
    }

    [Theory]
    [InlineData("SUSPENDED", "OWNER")]
    [InlineData("DEACTIVATED", "OWNER")]
    [InlineData("ACTIVE", "ASSISTANT")]
    public void PlaceholderOwnerDoesNotConsumeLastUsableOwnerProtection(string status, string role)
    {
        var target = new ManagedUser(4, "Seed Owner", "seed-owner", Roles.Owner, "ACTIVE", null, 0);
        Assert.Equal(UserWriteError.None, UserManagementRules.Check(1, target, Change(target, status, role), 1, false));
        Assert.Equal(UserWriteError.LastOwner, UserManagementRules.Check(1, target, Change(target, status, role), 1, true));
    }

    [Fact]
    public async Task RealOwnerCanDemotePlaceholderOwnerAndCountsReflectSavedRole()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); await Login(owner, "owner");
        var before = (await owner.GetFromJsonAsync<UserCounts>("/api/users/counts"))!;
        var response = await Send(owner, HttpMethod.Patch, "/api/users/4", Change(factory.Managed.Get(4), "ACTIVE", Roles.Assistant));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Roles.Assistant, factory.Users.Users[4].Role);
        var after = (await owner.GetFromJsonAsync<UserCounts>("/api/users/counts"))!;
        Assert.Equal(before.ActiveOwners - 1, after.ActiveOwners);
        Assert.Equal(before.ActiveAssistants + 1, after.ActiveAssistants);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/users")).StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        internal AuthApiTests.MemoryUsers Users { get; } = new(Password);
        internal MemoryManagement Managed { get; }
        public Factory() { Managed = new(Users); }
        internal HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IUserRepository>(); services.RemoveAll<IUserManagementRepository>();
                services.AddSingleton<IUserRepository>(Users); services.AddSingleton<IUserManagementRepository>(Managed);
                services.RemoveAll<IAuthSessionRepository>(); services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
            });
        }
    }

    // In-memory boundary for HTTP tests. PostgreSQL locking is covered only by opt-in DB tests.
    private sealed class MemoryManagement(AuthApiTests.MemoryUsers users) : IUserManagementRepository
    {
        private readonly Dictionary<int, string> statuses = [];
        public ManagedUser Get(int id)
        {
            var u = users.Users[id];
            return new(id, u.FullName, u.Username, u.Role, statuses.GetValueOrDefault(id) ?? (u.IsActive ? "ACTIVE" : "DEACTIVATED"), DateTimeOffset.UtcNow, u.AuthVersion);
        }
        private IEnumerable<ManagedUser> All => users.Users.Keys.Select(Get);
        public Task<ManagedUser?> FindAsync(int id, CancellationToken ct) => Task.FromResult(users.Users.ContainsKey(id) ? Get(id) : null);
        public Task<UserCounts> CountsAsync(CancellationToken ct) => Task.FromResult(new UserCounts(
            All.Count(u => u.Status == "ACTIVE" && u.Role == Roles.Owner), All.Count(u => u.Status == "ACTIVE" && u.Role == Roles.Assistant),
            All.Count(u => u.Status == "SUSPENDED"), All.Count(u => u.Status == "DEACTIVATED")));
        public Task<UserPage> ListAsync(string category, string search, int page, int pageSize, CancellationToken ct)
        {
            var list = All.Where(u => category switch { "owners" => u.Role == Roles.Owner && u.Status == "ACTIVE", "assistants" => u.Role == Roles.Assistant && u.Status == "ACTIVE", "suspended" => u.Status == "SUSPENDED", _ => u.Status == "DEACTIVATED" })
                .Where(u => u.Username.Contains(search, StringComparison.OrdinalIgnoreCase) || u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult(new UserPage(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize));
        }
        public Task<UserWriteResult> CreateAsync(int actorId, string name, string username, string hash, string role, CancellationToken ct)
        {
            if (All.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(new UserWriteResult(Error: UserWriteError.DuplicateUsername));
            var id = users.Users.Keys.Max() + 1;
            users.Users[id] = new(id, username, hash, name, role, true);
            return Task.FromResult(new UserWriteResult(Get(id)));
        }
        public Task<UserWriteResult> ChangeAsync(int actorId, int id, ChangeUserRequest request, CancellationToken ct)
        {
            if (!users.Users.ContainsKey(id)) return Task.FromResult(new UserWriteResult(Error: UserWriteError.NotFound));
            var hasher = new PasswordService();
            var error = UserManagementRules.Check(actorId, Get(id), request,
                users.Users.Values.Count(u => u.Role == Roles.Owner && u.IsActive && hasher.IsUsableHash(u.PasswordHash)),
                hasher.IsUsableHash(users.Users[id].PasswordHash));
            if (error != UserWriteError.None) return Task.FromResult(new UserWriteResult(Error: error));
            statuses[id] = request.Status;
            users.Users[id] = users.Users[id] with { Role = request.Role, IsActive = request.Status == "ACTIVE", AuthVersion = users.Users[id].AuthVersion + 1 };
            return Task.FromResult(new UserWriteResult(Get(id)));
        }
    }
}
