using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using WoodFlow.Domain;

namespace WoodFlow.Api.Security;

public static class AccountPrincipal
{
    public const string CredentialStamp = "credential_stamp";
    public const string SessionHandle = "session_handle";

    public static string Stamp(User user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{user.AuthVersion}:{user.PasswordHash}")));

    public static ClaimsPrincipal Create(User user, string sessionHandle) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim(SessionHandle, sessionHandle),
        new Claim(CredentialStamp, Stamp(user))
    ], CookieAuthenticationDefaults.AuthenticationScheme));
}
