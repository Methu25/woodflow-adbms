using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodFlow.Api.Security;
using WoodFlow.Application.Interfaces;
using WoodFlow.Application.Services;
using WoodFlow.Infrastructure;
using WoodFlow.Infrastructure.Repositories;
using WoodFlow.Infrastructure.Security;

var generateSetupCode = args.Contains("--generate-owner-setup-code", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args
    .Where(arg => arg != "--generate-owner-setup-code").ToArray());

builder.Services.AddControllers(options => options.Filters.Add<CsrfFilter>())
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ =>
        new BadRequestObjectResult(new { message = "Invalid request." }));
builder.Services.AddSingleton(_ => new Database(builder.Configuration.GetConnectionString("WoodFlowDb")));
builder.Services.AddSingleton<IHealthRepository, PostgresHealthRepository>();
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<IUserManagementRepository, UserManagementRepository>();
builder.Services.AddSingleton<UserManagementService>();
builder.Services.AddSingleton<IPermissionRepository, PermissionRepository>();
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<ISetupRepository, SetupRepository>();
builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
builder.Services.AddSingleton<IAuthSessionRepository, AuthSessionRepository>();
builder.Services.AddSingleton<AccountSessionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountLoginThrottle>();
builder.Services.AddSingleton<ISetupService, SetupService>();
builder.Services.AddSingleton<IRecoveryRepository, RecoveryRepository>();
builder.Services.AddSingleton<RecoveryService>();
builder.Services.AddSingleton<IFinishedFurnitureRepository, FinishedFurnitureRepository>();
builder.Services.AddSingleton<FinishedFurnitureService>();
builder.Services.AddSingleton<IEmployeeAdvanceRepository, EmployeeAdvanceRepository>();
builder.Services.AddSingleton<EmployeeAdvanceService>();
builder.Services.AddScoped<AccountCookieEvents>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "__Host-WoodFlow.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-WoodFlow.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.EventsType = typeof(AccountCookieEvents);
    });
builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Public recovery has both per-IP and installation-wide caps. Do not trust arbitrary forwarded IP headers.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName switch
        {
            "login" => RateLimitPartition.GetFixedWindowLimiter("public-login", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }),
            "recovery-request" or "recovery-reset" => RateLimitPartition.GetFixedWindowLimiter("public-recovery", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }),
            _ => RateLimitPartition.GetNoLimiter("other")
        });
    foreach (var (name, limit, minutes) in new[]
        { ("recovery-request", 10, 10), ("recovery-reset", 10, 1), ("recovery-approve", 10, 1) })
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
                { PermitLimit = limit, Window = TimeSpan.FromMinutes(minutes), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    // Installation-wide cap prevents distributed guesses of bootstrap authorization.
    options.AddFixedWindowLimiter("setup", limiter =>
    {
        limiter.PermitLimit = 3;
        limiter.Window = TimeSpan.FromMinutes(15);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
if (generateSetupCode)
{
    try
    {
        var code = await app.Services.GetRequiredService<ISetupService>().GenerateCodeAsync(CancellationToken.None);
        if (code is null)
        {
            Console.Error.WriteLine("Setup authorization could not be issued.");
            Environment.ExitCode = 1;
        }
        else
        {
            // This is the sole intentional disclosure, to the trusted server administrator.
            Console.WriteLine("One-time setup code (expires in 10 minutes; keep private):");
            Console.WriteLine(code);
        }
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Setup authorization could not be issued. Check configuration and manual migrations.");
        Environment.ExitCode = 1;
    }
    await app.DisposeAsync();
    return;
}

app.UseMiddleware<RequestSecurityMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
