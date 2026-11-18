using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoodFlow.Api.Security;
using WoodFlow.Application.Interfaces;
using WoodFlow.Domain;
using WoodFlow.Infrastructure.Security;
using Xunit;

namespace WoodFlow.Tests;

public sealed class AuthApiTests
{
    private const string Password = "Example local test password 42!";
    private static object Login(string username, string password = Password) => new { username, password };
    private static object Setup => new
    {
        fullName = "Test Owner", username = "new-owner", password = Password,
        confirmPassword = Password, setupCode = new string('A', 64)
    };

    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("XSRF-TOKEN="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        return Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task SignIn(HttpClient client, string username)
    {
        var response = await Post(client, "/api/auth/login", Login(username), await Csrf(client));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginCookieSafeMeAndLogout()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var token = await Csrf(client);
        var login = await Post(client, "/api/auth/login", Login("owner"), token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Host-WoodFlow.Auth="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var me = await client.GetStringAsync("/api/auth/me");
        Assert.Contains("\"role\":\"OWNER\"", me);
        Assert.DoesNotContain("password", me, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", me, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/owner")).StatusCode);

        // Anonymous anti-forgery tokens cannot be reused after login.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(client, "/api/auth/logout", new { }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(client, "/api/auth/logout", new { }, await Csrf(client))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/logout")]
    [InlineData("/api/setup/initialize-owner")]
    public async Task StateChangesRequireCsrf(string path)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        if (path.EndsWith("logout"))
            await SignIn(client, "owner");
        var response = await client.PostAsJsonAsync(path, path.Contains("setup") ? Setup : Login("owner"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("placeholder")]
    [InlineData("legacy-role")]
    public async Task InvalidInactivePlaceholderAndOldRolesCannotLogin(string username)
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var response = await Post(client, "/api/auth/login", Login(username), await Csrf(client));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"message\":\"Unable to sign in.\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IncorrectPasswordCannotLogin()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Post(client, "/api/auth/login", Login("owner", "incorrect"), await Csrf(client))).StatusCode);
    }

    [Fact]
    public async Task RolesStatusAndPasswordAreRevalidated()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        await SignIn(client, "assistant");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test/owner")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/business")).StatusCode);
        factory.Users.Users[2] = factory.Users.Users[2] with { Role = Roles.Owner };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/owner")).StatusCode);
        factory.Users.Users[2] = factory.Users.Users[2] with { Role = Roles.Assistant };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test/owner")).StatusCode);
        factory.Users.Users[2] = factory.Users.Users[2] with { IsActive = false };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        factory.Users.Users[2] = factory.Users.Users[2] with { IsActive = true };
        await SignIn(client, "assistant");
        var current = factory.Users.Users[2];
        factory.Users.Users[2] = current with
        {
            PasswordHash = new PasswordService().Hash(current, "Changed secure password 42!")
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        factory.Users.Users[2] = current;
        await SignIn(client, "assistant");
        factory.Users.Users[2] = factory.Users.Users[2] with { PasswordHash = "hash_placeholder" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task HttpAuthAndSetupAreRejectedAndHealthStillWorks()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false
        });
        foreach (var path in new[] { "/api/auth/csrf", "/api/auth/me" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/login", Login("owner"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/setup/initialize-owner", Setup)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task LoginAndSetupAreRateLimited()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var token = await Csrf(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await Post(client, "/api/auth/login", Login("missing"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Post(client, "/api/auth/login", Login("missing"), token)).StatusCode);
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.BadRequest,
                (await Post(client, "/api/setup/initialize-owner", Setup, token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Post(client, "/api/setup/initialize-owner", Setup, token)).StatusCode);
    }

    [Fact]
    public async Task BadSetupCodeReturnsGenericFailure()
    {
        await using var factory = new Factory();
        using var client = factory.Client();
        var response = await Post(client, "/api/setup/initialize-owner", Setup, await Csrf(client));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"message\":\"Unable to initialize account.\"}", await response.Content.ReadAsStringAsync());
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public MemoryUsers Users { get; } = new(Password);
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IUserRepository>();
                services.RemoveAll<ISetupRepository>();
                services.RemoveAll<IHealthRepository>();
                services.AddSingleton<IUserRepository>(Users);
                services.RemoveAll<IAuthSessionRepository>(); services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
                services.AddSingleton<ISetupRepository, RejectSetup>();
                services.AddSingleton<IHealthRepository, Healthy>();
                services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
            });
        }
    }

    private sealed class Healthy : IHealthRepository
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RejectSetup : ISetupRepository
    {
        public Task<bool> IssueCodeAsync(byte[] hash, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> InitializeOwnerAsync(string name, string username, string password, byte[] hash,
            CancellationToken ct) => Task.FromResult(false);
    }

    internal sealed class MemoryUsers : IUserRepository
    {
        public Dictionary<int, User> Users { get; } = [];
        public MemoryUsers(string password)
        {
            var hasher = new PasswordService();
            foreach (var (id, username, role, active) in new[]
            {
                (1, "owner", Roles.Owner, true), (2, "assistant", Roles.Assistant, true),
                (3, "inactive", Roles.Owner, false), (4, "placeholder", Roles.Owner, true),
                (5, "legacy-role", "CASHIER", true)
            })
            {
                var user = new User(id, username, "", "Test User", role, active);
                Users[id] = user with { PasswordHash = id == 4 ? "hash_owner_123" : hasher.Hash(user, password) };
            }
        }
        public Task<User?> FindByIdAsync(int id, CancellationToken ct) =>
            Task.FromResult(Users.GetValueOrDefault(id));
        public Task<User?> FindByUsernameAsync(string name, CancellationToken ct) =>
            Task.FromResult(Users.Values.SingleOrDefault(u => u.Username == name));
        public Task UpdateHashAsync(int id, string oldHash, string newHash, CancellationToken ct)
        {
            if (Users[id].PasswordHash == oldHash)
                Users[id] = Users[id] with { PasswordHash = newHash };
            return Task.CompletedTask;
        }
    }
}

// Loaded only by the test host, never included in the production API.
[ApiController]
[Route("test")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("owner"), Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    public IActionResult Owner() => Ok();
    [HttpGet("business"), Authorize(Policy = AuthorizationPolicies.BusinessAccess)]
    public IActionResult Business() => Ok();
}
