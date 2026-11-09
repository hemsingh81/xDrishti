namespace XDrishti.Application.SettingsCatalog;

/// <summary>
/// The descriptors of the settings that are not in the <c>Confidence</c> section (the Api registers those key by key): the Essential five, the owner switches stored in the database, and one Internal
/// descriptor for every other configuration section bound at start-up. Moving a setting between tiers is a change here only; nothing about its validation or default moves with it.
/// </summary>
public static class SettingsRegistry
{
    /// <summary>The most descriptors that may be <c>Essential</c> (ADR 0242).</summary>
    public const int MaxEssential = 8;

    private static SettingDescriptor Stored(string key, string label, SettingTier tier, string? @default) =>
        new(key, label, tier, SettingKind.Stored, @default, null, null, SettingSafer.None, false);

    private static SettingDescriptor Section(string key) =>
        new(key, key.Replace(':', ' '), SettingTier.Internal, SettingKind.Section, null, null, null, SettingSafer.None, false);

    /// <summary>The Essential five and the stored owner switches. Every switch is OFF until the owner turns it on.</summary>
    public static IReadOnlyList<SettingDescriptor> StoredSettings { get; } =
    [
        Stored("auto.enabled", "Auto", SettingTier.Essential, "false"),
        Stored("suggest.scope", "What to suggest", SettingTier.Essential, "eligible"),
        Stored("risk.profile", "Risk profile", SettingTier.Essential, null),
        Stored("chargesheet.gate", "Charge sheet", SettingTier.Essential, "false"),
        Stored("notices.failure", "Failure notices", SettingTier.Essential, "true"),
        Stored("autonomy.auto_approve_tickets", "Approve paper tickets automatically", SettingTier.Advanced, "false"),
        Stored("autonomy.promote_from_reserve", "Promote from the reserve", SettingTier.Advanced, "false"),
        Stored("confidence.kill.auto", "Apply kills automatically", SettingTier.Advanced, "false"),
        Stored("confidence.readiness.enforce", "Enforce the readiness gate", SettingTier.Advanced, "false"),
        Stored("live.auto_approve_tickets", "Approve live tickets automatically", SettingTier.Advanced, "false"),
        Stored("live.auto_ramp_up", "Ramp up automatically", SettingTier.Advanced, "false"),
        Stored("ml.influence.enabled", "Let the model influence suggestions", SettingTier.Advanced, "false"),
        Stored("ml.auto_promote", "Promote models automatically", SettingTier.Advanced, "false"),
        Stored("ml.auto_rollback", "Roll models back automatically", SettingTier.Advanced, "false"),
    ];

    /// <summary>One Internal descriptor per configuration section bound at start-up, except <c>Confidence</c> (registered key by key). The value of a section is never shown.</summary>
    public static IReadOnlyList<SettingDescriptor> Sections { get; } =
    [
        .. new[]
        {
            "Live", "Live:Buckets", "Live:Estimates", "Live:Capacity", "Live:Report", "Live:Ramp", "Live:Capture", "Live:Match", "Live:Protect",
            "Ticks", "Depth", "Options", "Options:Liquidity", "Options:Costs", "Options:PhaseCheck", "Options:Remark", "Options:Quality",
            "Pairs", "Models", "Filings", "Experiments", "Recovery", "Shadow", "Heartbeat", "Exports", "Mcp", "Eval",
            "Planning:TicketAlerts", "Reports:PlanVsActual", "Reports:Benchmark",
            "Portfolio:Heal", "Portfolio:BrokerHistory", "Portfolio:AutoRefresh", "Instruments:AutoRefresh", "Data", "Data:History",
            "Assistant:Mcp", "Assistant:Llm", "Assistant:Briefing", "Assistant:Turn", "Assistant:Proposals", "Assistant:Drafts", "AiRoles",
            "Brokers", "BrokerVault", "ExecHandover", "ExecRole", "Execution", "Execution:Sandbox", "Database", "Auth:Owner",
        }.Select(Section),
    ];

    /// <summary>Every descriptor of this registry.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } = [.. StoredSettings, .. Sections];
}
