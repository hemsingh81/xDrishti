namespace XDrishti.Api.Endpoints;

/// <summary>The owner's change of the sizing settings. A value left out stays as it is. The password is needed for every change. The actor is never taken from the body.</summary>
internal sealed record SizingSettingsChangeBody(bool? VolTargetEnabled, double? Floor, bool? CapacityEnabled, double? CapacityPct, string? Password, double? MinFit = null);

/// <summary>The owner's change of one basket's risk budget: 1 to 100 percent of the total open risk limit, or null to remove it. The password is needed for every change. The actor is never taken from the body.</summary>
internal sealed record BasketRiskBudgetChangeBody(double? BudgetPct, string? Password);

/// <summary>The owner change of the basket settings (BK-08). A value left out stays as it is; clearCap true turns the cluster cap off. The password is needed for every change. The actor is never taken from the body.</summary>
internal sealed record BasketSettingsChangeBody(string? BreadthSource, double? MaxClusterRiskPct, bool? ClearCap, double? ClusterRho, string? Password);
