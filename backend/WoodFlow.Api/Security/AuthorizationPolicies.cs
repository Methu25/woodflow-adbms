using WoodFlow.Domain;

namespace WoodFlow.Api.Security;

public static class AuthorizationPolicies
{
    public const string OwnerOnly = "OwnerOnly";
    public const string BusinessAccess = "BusinessAccess";

    public static void Configure(Microsoft.AspNetCore.Authorization.AuthorizationOptions options)
    {
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().RequireRole(Roles.Owner, Roles.Assistant).Build();
        options.AddPolicy(OwnerOnly, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Owner));
        options.AddPolicy(BusinessAccess, policy =>
            policy.RequireAuthenticatedUser().RequireRole(Roles.Owner, Roles.Assistant));
        foreach (var key in PermissionCatalog.Keys)
            options.AddPolicy(key, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Owner, Roles.Assistant)
                .AddRequirements(new PermissionRequirement(key)));
    }
}
