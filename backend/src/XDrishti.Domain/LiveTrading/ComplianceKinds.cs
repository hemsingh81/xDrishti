namespace XDrishti.Domain.LiveTrading;

/// <summary>The three kinds of the compliance record (spec P11-05) and the outcomes each may carry. Stored by these words; never rename one.</summary>
public static class ComplianceKinds
{
    public const string StaticIp = "static_ip";
    public const string SebiRecheck = "sebi_recheck";
    public const string DhanIpReadPolicy = "dhan_ip_read_policy";

    public static IReadOnlyList<string> All { get; } = [StaticIp, SebiRecheck, DhanIpReadPolicy];

    private static readonly Dictionary<string, string[]> Outcomes = new(StringComparer.Ordinal)
    {
        [StaticIp] = ["not_needed_for_manual", "requested", "obtained", "whitelisted", "declined", "blocked"],
        [SebiRecheck] = ["rechecked_no_change", "rechecked_changed", "blocked"],
        [DhanIpReadPolicy] = ["reads_not_ip_restricted", "reads_ip_restricted", "unanswered"],
    };

    /// <summary>The outcomes allowed for a kind; empty for an unknown kind.</summary>
    public static IReadOnlyList<string> OutcomesOf(string? kind) => kind is not null && Outcomes.TryGetValue(kind, out var list) ? list : [];

    /// <summary>The words the owner reads for a kind.</summary>
    public static string Label(string kind) => kind switch
    {
        StaticIp => "Static IP",
        SebiRecheck => "SEBI re-check",
        DhanIpReadPolicy => "Dhan IP rule for reads",
        _ => kind,
    };
}

/// <summary>The states of the static IP the owner writes down (not checked against anything).</summary>
public static class StaticIpStates
{
    public const string None = "none";
    public const string Requested = "requested";
    public const string Active = "active";
    public const string Whitelisted = "whitelisted";

    public static IReadOnlyList<string> All { get; } = [None, Requested, Active, Whitelisted];
}
