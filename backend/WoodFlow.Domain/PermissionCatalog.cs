namespace WoodFlow.Domain;

public sealed record PermissionDefinition(string Key, string Action);
public sealed record PermissionModule(string Key, string Label, string Description, IReadOnlyList<PermissionDefinition> Permissions);

public static class PermissionCatalog
{
    public const string DashboardView = "dashboard.view";
    public const string SalesView = "sales.view";
    public static readonly IReadOnlyList<PermissionModule> Modules = Array.AsReadOnly(new[]
    {
        Module("dashboard", "Dashboard", "Welcome dashboard navigation", "view"),
        Module("sales", "Sales & Billing", "Billing access; posted business records must be retained", "view", "create", "edit"),
        Module("quotations", "Quotations", "Quotation access; deletion is for eligible drafts only", "view", "create", "edit", "delete"),
        Module("inventory", "Inventory", "Inventory access; deletion must respect business history", "view", "create", "edit", "delete"),
        Module("production", "Production", "Production access; historical work records must be retained", "view", "create", "edit"),
        Module("reports", "Reports", "Read business reports", "view")
    });
    public static readonly IReadOnlySet<string> Keys = new HashSet<string>(Modules.SelectMany(m => m.Permissions).Select(p => p.Key), StringComparer.Ordinal);
    private static PermissionModule Module(string key, string label, string description, params string[] actions) =>
        new(key, label, description, Array.AsReadOnly(actions.Select(a => new PermissionDefinition($"{key}.{a}", a)).ToArray()));
}
