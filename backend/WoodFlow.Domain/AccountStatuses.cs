namespace WoodFlow.Domain;

public static class AccountStatuses
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Deactivated = "DEACTIVATED";
    public static bool IsSupported(string value) => value is Active or Suspended or Deactivated;
}
