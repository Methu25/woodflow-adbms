using System.Net;
using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Application.DTOs;
using Xunit;

namespace WoodFlow.Tests;

public sealed class SessionAndThrottleTests
{
    // Generated for isolated in-memory test accounts only; never a database credential.
    private static readonly string TestOnlyPassword =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string ip = "198.51.100.1")
    {
        var csrf = await client.GetAsync("/api/auth/csrf"); csrf.EnsureSuccessStatusCode();
        var token = Uri.UnescapeDataString(csrf.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="))
            .Split(';')[0]["XSRF-TOKEN=".Length..]);
        using var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        r.Headers.Add("X-XSRF-TOKEN", token); r.Headers.Add("X-Test-IP", ip);
        return await client.SendAsync(r);
    }
    private static Task<HttpResponseMessage> Login(HttpClient c, string? password = null, string ip = "198.51.100.1", string username = "owner") =>
        Post(c, "/api/auth/login", new { username, password = password ?? TestOnlyPassword }, ip);

    [Fact]
    public async Task LogoutRevokesCopiedTicketButKeepsOtherSession()
    {
        await using var f = new Factory(); using var first = f.Client(); using var second = f.Client();
        var login = await Login(first); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(second)).StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-WoodFlow.Auth=")).Split(';')[0];
        using var copied = f.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        copied.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await copied.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(first, "/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
        var user = f.Users.Users[1];
        f.Users.Users[1] = user with { AuthVersion = user.AuthVersion + 1 };
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RevocationOutageDoesNotFalselyReportSuccessfulLogout()
    {
        await using var f = new Factory(); using var c = f.Client();
        Assert.Equal(HttpStatusCode.OK, (await Login(c)).StatusCode);
        ((TestAuthSessions)f.Services.GetRequiredService<IAuthSessionRepository>()).FailRevocation = true;
        var response = await Post(c, "/api/auth/logout", new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("missing")]
    public async Task UsernameLimitSpansDifferentIpsAndAppliesEquallyToMissingAccounts(string username)
    {
        await using var f = new Factory(); using var c = f.Client();
        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "incorrect test password", $"198.51.100.{i}", username)).StatusCode);
        var blocked = await Login(c, TestOnlyPassword, "198.51.100.11", username);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        Assert.DoesNotContain(username, await blocked.Content.ReadAsStringAsync());
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(username == "owner" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            (await Login(c, TestOnlyPassword, "198.51.100.12", username)).StatusCode);
        Assert.True(f.Users.Users[1].IsActive); // No persistent account lockout/status change.
    }

    [Fact]
    public void BucketIsCaseInsensitiveBoundedConcurrentAndDoesNotExtendCooldown()
    {
        var clock = new Clock(); var throttle = new AccountLoginThrottle(clock);
        var admitted = 0;
        Parallel.For(0, 100, _ => { if (throttle.RetryAfterSeconds(" Owner ") == 0) Interlocked.Increment(ref admitted); });
        Assert.Equal(10, admitted);
        Assert.Equal(6, throttle.RetryAfterSeconds("OWNER"));
        clock.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(4, throttle.RetryAfterSeconds("owner"));
        clock.Advance(TimeSpan.FromSeconds(4)); Assert.Equal(0, throttle.RetryAfterSeconds("owner"));
        for (var i = 0; i < 4999; i++) Assert.Equal(0, throttle.RetryAfterSeconds("fixture-" + i));
        Assert.Equal(60, throttle.RetryAfterSeconds("new-fixture"));
        Assert.True(throttle.RetryAfterSeconds("owner") > 0); // Active victim entry was not evicted.
        clock.Advance(TimeSpan.FromMinutes(10)); Assert.Equal(0, throttle.RetryAfterSeconds("new-fixture"));
    }

    [Fact]
    public async Task ExpiredServerSessionCannotAuthenticate()
    {
        await using var f = new Factory(); using var c = f.Client();
        Assert.Equal(HttpStatusCode.OK, (await Login(c)).StatusCode);
        f.Clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData(7, false, false)]
    [InlineData(8, true, false)]
    [InlineData(11, true, false)]
    [InlineData(12, true, true)]
    [InlineData(128, true, true)]
    [InlineData(129, false, false)]
    public void ExistingPasswordLimitsRemainUnchanged(int length, bool standardValid, bool setupValid)
    {
        var password = new string('A', length);
        bool Valid(object value) => Validator.TryValidateObject(value, new ValidationContext(value), [], true);
        Assert.Equal(standardValid, Valid(new CreateUserRequest { FullName = "Fixture", Username = "fixture", Role = "ASSISTANT", Password = password }));
        Assert.Equal(standardValid, Valid(new CompleteRecoveryRequest { Code = new string('B', 64), NewPassword = password, ConfirmPassword = password }));
        Assert.Equal(setupValid, Valid(new InitializeOwnerRequest { FullName = "Fixture", Username = "fixture", Password = password, ConfirmPassword = password, SetupCode = new string('B', 64) }));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public Clock Clock { get; } = new();
        public AuthApiTests.MemoryUsers Users { get; } = new(TestOnlyPassword);
        public HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IUserRepository>(); services.AddSingleton<IUserRepository>(Users);
            services.RemoveAll<IAuthSessionRepository>(); services.AddSingleton<IAuthSessionRepository, TestAuthSessions>();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter, TestIp>();
        });
    }
    // Test-only IP injection. This header is never trusted by the production API.
    private sealed class TestIp : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            Microsoft.AspNetCore.Builder.UseExtensions.Use(app, async (context, nextRequest) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-IP"], out var ip)) context.Connection.RemoteIpAddress = ip;
                await nextRequest();
            });
            next(app);
        };
    }
}
