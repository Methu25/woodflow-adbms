namespace WoodFlow.Domain;

public static class Roles
{
    public const string Owner = "OWNER";
    public const string Assistant = "ASSISTANT";
    public static bool IsSupported(string role) => role is Owner or Assistant;
}
