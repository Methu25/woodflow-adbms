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

// Real cookies, CSRF, OWNER policies and permission authorization handler. No PostgreSQL access.
public sealed class PermissionTests
{
    private const string Password = "Permission test password 42!";
    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf"); response.EnsureSuccessStatusCode();
        return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="))
            .Split(';')[0]["XSRF-TOKEN=".Length..]);
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-XSRF-TOKEN", await Csrf(client));
        return await client.SendAsync(request);
    }
    private static async Task Login(HttpClient client, string username)
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/auth/login", new { username, password = Password })).StatusCode);
    }
    private static object Save(long version, params string[] keys) => new { expectedVersion = version, grantedKeys = keys, confirmed = true };

    [Fact]
    public async Task OnlyOwnerCanReadAndWriteAssistantSettingsAndCsrfIsRequired()
    {
        await using var factory = new Factory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/permissions/assistant")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/permissions/check/dashboard.view")).StatusCode);
        await Login(client, "assistant");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/permissions/assistant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Put, "/api/permissions/assistant", Save(0))).StatusCode);
        using var owner = factory.Client(); await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/permissions/assistant")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0), false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0))).StatusCode);
        var effective = (await owner.GetFromJsonAsync<EffectivePermissions>("/api/permissions/me"))!;
        Assert.Equal(PermissionCatalog.Keys.Count, effective.GrantedKeys.Length); // OWNER remains fixed, even with zero ASSISTANT grants.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.GetAsync("/api/permissions/check/sales.view")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/permissions/check/users.manage")).StatusCode);
    }

    [Fact]
    public async Task ExistingAssistantCookieUsesLatestPermissionsOnEveryCheck()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); using var assistant = factory.Client();
        await Login(owner, "owner"); await Login(assistant, "assistant");
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/dashboard.view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/check/sales.view")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0, "sales.view", "sales.create"))).StatusCode);
        // Same authenticated client; no re-login or new cookie.
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/sales.view")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/check/dashboard.view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/assistant")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await assistant.GetAsync("/api/auth/me")).StatusCode); // No grants does not destroy the session/logout ability.
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Empty((await assistant.GetFromJsonAsync<EffectivePermissions>("/api/permissions/me"))!.GrantedKeys);
    }

    [Fact]
    public async Task StaleSavesAndAdministrativeOrInconsistentKeysAreRejected()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); await Login(owner, "owner");
        foreach (var keys in new[] { new[] { "users.manage" }, new[] { "audit.view" }, new[] { "permissions.edit" }, new[] { "sales.create" }, new[] { "dashboard.view", "dashboard.view" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0, keys))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", new { expectedVersion = 0, grantedKeys = Array.Empty<string>(), confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0, "reports.view"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0, "dashboard.view"))).StatusCode);
        Assert.Equal(new[] { "reports.view" }, (await owner.GetFromJsonAsync<PermissionMatrix>("/api/permissions/assistant"))!.GrantedKeys);
    }

    [Fact]
    public void CatalogContainsOnlyBusinessKeysAndSensibleActionDependencies()
    {
        Assert.Equal(6, PermissionCatalog.Modules.Count);
        Assert.True(PermissionService.Valid([]));
        Assert.True(PermissionService.Valid(PermissionCatalog.Keys.ToArray()));
        Assert.False(PermissionService.Valid(["inventory.edit"]));
        Assert.False(PermissionService.Valid(["sales.delete"]));
        Assert.False(PermissionService.Valid(["security.edit"]));
    }

    private static object SaveUser(long version, long roleVersion, bool defaults, params string[] keys) => new
    { grantedKeys = keys, expectedVersion = version, expectedRoleVersion = roleVersion, expectedAccountVersion = 0, useRoleDefaults = defaults, confirmed = true };

    [Fact]
    public async Task IndividualOverridesAreOwnerOnlyAndCannotTargetOwners()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); using var assistant = factory.Client();
        await Login(owner, "owner"); await Login(assistant, "assistant");
        var original = (await owner.GetFromJsonAsync<UserPermissionMatrix>("/api/permissions/users/2"))!;
        Assert.True(original.InheritsRole); Assert.Equal(new[] { "dashboard.view" }, original.GrantedKeys);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/users/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(assistant, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/permissions/users/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(owner, HttpMethod.Put, "/api/permissions/users/1", SaveUser(0, 0, false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false), false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false, "audit.view"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false, "sales.create"))).StatusCode);
    }

    [Fact]
    public async Task OverrideIsIsolatedFromOtherAssistantsAndResetRestoresCurrentDefaults()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); using var assistant = factory.Client(); using var second = factory.Client();
        await Login(owner, "owner"); await Login(assistant, "assistant"); await Login(second, "assistant2");
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false, "sales.view", "sales.create"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/check/dashboard.view")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second.GetAsync("/api/permissions/check/dashboard.view")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put, "/api/permissions/assistant", Save(0, "reports.view"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second.GetAsync("/api/permissions/check/reports.view")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(1, 0, true))).StatusCode);
        var reset = await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(1, 1, true));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var restored = (await reset.Content.ReadFromJsonAsync<UserPermissionSettings>())!;
        Assert.True(restored.InheritsRole); Assert.Equal(2, restored.Version);
        Assert.Equal(HttpStatusCode.Forbidden, (await assistant.GetAsync("/api/permissions/check/sales.create")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assistant.GetAsync("/api/permissions/check/reports.view")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 1, false))).StatusCode);
    }

    [Fact]
    public async Task DeactivatedAccountsCannotReceiveOverrides()
    {
        await using var factory = new Factory(); using var owner = factory.Client(); await Login(owner, "owner");
        factory.Users.Users[2] = factory.Users.Users[2] with { IsActive = false };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, HttpMethod.Put, "/api/permissions/users/2", SaveUser(0, 0, false))).StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        internal AuthApiTests.MemoryUsers Users { get; } = new(Password);
        private readonly MemoryPermissions permissions;
        public Factory()
        {
            permissions = new(Users);
            var user = new User(6, "assistant2", "", "Second Assistant", Roles.Assistant, true);
            Users.Users[6] = user with { PasswordHash = new PasswordService().Hash(user, Password) };
        }
        internal HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IUserRepository>(); services.RemoveAll<IPermissionRepository>();
                services.AddSingleton<IUserRepository>(Users); services.AddSingleton<IPermissionRepository>(permissions);
                services.RemoveAll<IAuthSessionRepository>(); services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
            });
        }
    }
    private sealed class MemoryPermissions(AuthApiTests.MemoryUsers users) : IPermissionRepository
    {
        private PermissionSettings current = new(["dashboard.view"], 0);
        private readonly Dictionary<int, (string[]? Keys, long Version)> overrides = [];
        public Task<PermissionSettings> GetAsync(CancellationToken ct) => Task.FromResult(current);
        public Task<PermissionSaveResult> SaveAsync(int actorId, string[] keys, long expectedVersion, CancellationToken ct)
        {
            if (expectedVersion != current.Version) return Task.FromResult(new PermissionSaveResult(Error: PermissionSaveError.Conflict));
            current = new(keys, current.Version + 1); return Task.FromResult(new PermissionSaveResult(current));
        }
        public async Task<PermissionSettings> GetEffectiveAsync(int id, CancellationToken ct)
        {
            var state = (await GetUserAsync(id, ct))!; return new(state.GrantedKeys, state.Version);
        }
        public Task<UserPermissionSettings?> GetUserAsync(int id, CancellationToken ct)
        {
            var user = users.Users.GetValueOrDefault(id);
            if (user?.Role != Roles.Assistant) return Task.FromResult<UserPermissionSettings?>(null);
            var row = overrides.GetValueOrDefault(id);
            return Task.FromResult<UserPermissionSettings?>(new(new(user.UserId, user.FullName, user.Username, user.Role,
                user.IsActive ? "ACTIVE" : "DEACTIVATED", null, user.AuthVersion), row.Keys ?? current.GrantedKeys, row.Keys is null, row.Version, current.Version));
        }
        public async Task<UserPermissionSaveResult> SaveUserAsync(int actorId, int id, SaveUserPermissionsRequest request, CancellationToken ct)
        {
            var before = await GetUserAsync(id, ct);
            if (before is null) return new(Error: PermissionSaveError.NotFound);
            if (before.User.Status == "DEACTIVATED") return new(Error: PermissionSaveError.Deactivated);
            if (before.Version != request.ExpectedVersion || before.RoleVersion != request.ExpectedRoleVersion || before.User.Version != request.ExpectedAccountVersion)
                return new(Error: PermissionSaveError.Conflict);
            overrides[id] = (request.UseRoleDefaults ? null : request.GrantedKeys, before.Version + 1);
            return new(await GetUserAsync(id, ct));
        }
    }
}
