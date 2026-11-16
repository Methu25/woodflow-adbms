namespace WoodFlow.Domain;

// Internal account record; never serialize this entity as an API response.
public sealed record User(int UserId, string Username, string PasswordHash,
    string FullName, string Role, bool IsActive, int AuthVersion = 0)
{
    public override string ToString() => $"User {UserId}";
}
