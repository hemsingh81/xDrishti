using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.SettingsCatalog;

/// <summary>
/// The descriptors of the settings that are not in the <c>Confidence</c> section (the Api registers those key by key): the Essential five, the owner switches stored in the database, and one Internal
/// descriptor for every other configuration section bound at start-up. Moving a setting between tiers is a change here only; nothing about its validation or default moves with it.
/// </summary>
public static class SettingsRegistry
{
    private static class QualificationDefaults
    {
        private static readonly QualificationGates G = QualificationGates.Default;
        public static readonly string MinOosTrades = G.MinOosTrades.ToString(CultureInfo.InvariantCulture);
        public static readonly string MinExpectancyR = G.MinExpectancyR.ToString(CultureInfo.InvariantCulture);
        public static readonly string MinProfitFactor = G.MinProfitFactor.ToString(CultureInfo.InvariantCulture);
        public static readonly string MinPositiveFoldsPct = G.MinPositiveFoldsPct.ToString(CultureInfo.InvariantCulture);
        public static readonly string MaxDrawdownR = G.MaxDrawdownR.ToString(CultureInfo.InvariantCulture);
        public static readonly string MaxCalibrationErrorPp = G.MaxCalibrationErrorPp.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The most descriptors that may be <c>Essential</c> (ADR 0242).</summary>
    public const int MaxEssential = 8;

    private static SettingDescriptor Stored(string key, string label, SettingTier tier, string? @default) =>
        new(key, label, tier, SettingKind.Stored, @default, null, null, SettingSafer.None, false);

    private static SettingDescriptor Config(string key, string label, SettingTier tier, string? @default, decimal? min = null, decimal? max = null, SettingSafer safer = SettingSafer.None) =>
        new(key, label, tier, SettingKind.Configuration, @default, min, max, safer, false);

    private static SettingDescriptor Section(string key) =>
        new(key, key.Replace(':', ' '), SettingTier.Internal, SettingKind.Section, null, null, null, SettingSafer.None, false);

    /// <summary>The Essential five and the stored owner switches. <c>risk.profile</c> and <c>chargesheet.gate</c> are read and changed through their existing screens (AU-09 links to them); the catalog shows no value for any stored setting. Every switch is OFF until the owner turns it on.</summary>
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
        Stored("execution_enabled", "Global execution switch (owner only, password every change)", SettingTier.Advanced, "false"),
        Stored("options.backfill_stocks", "Frozen share order of the options backfill", SettingTier.Internal, null),
    ];

    /// <summary>
    /// Configuration keys outside the Confidence section that a gate or a switch reads. They are read-only here (the catalog shows the configured value; nothing in this slice changes a value or the path that reads it).
    /// The qualification gates are tighten-only by their meaning: a minimum is stricter higher, a maximum stricter lower. <c>Live:Writes:Enabled</c> is the live deployment gate, OFF by default.
    /// </summary>
    public static IReadOnlyList<SettingDescriptor> ConfigurationSettings { get; } =
    [
        Config("StrategyLab:Gates:MinOosTrades", "Minimum out-of-sample trades", SettingTier.Advanced, QualificationDefaults.MinOosTrades, 0, null, SettingSafer.Higher),
        Config("StrategyLab:Gates:MinExpectancyR", "Minimum expectancy (R)", SettingTier.Advanced, QualificationDefaults.MinExpectancyR, 0, null, SettingSafer.Higher),
        Config("StrategyLab:Gates:MinProfitFactor", "Minimum profit factor", SettingTier.Advanced, QualificationDefaults.MinProfitFactor, 1, null, SettingSafer.Higher),
        Config("StrategyLab:Gates:MinPositiveFoldsPct", "Minimum share of positive folds (%)", SettingTier.Advanced, QualificationDefaults.MinPositiveFoldsPct, 0, 100, SettingSafer.Higher),
        Config("StrategyLab:Gates:MaxDrawdownR", "Maximum drawdown (R)", SettingTier.Advanced, QualificationDefaults.MaxDrawdownR, 0, null, SettingSafer.Lower),
        Config("StrategyLab:Gates:MaxCalibrationErrorPp", "Maximum calibration error (pp)", SettingTier.Advanced, QualificationDefaults.MaxCalibrationErrorPp, 0, null, SettingSafer.Lower),
        Config("Live:Writes:Enabled", "Live writes deployment gate", SettingTier.Advanced, "false"),
        Config("Learned:WeeklyCap", "Most automatic learned changes per week", SettingTier.Internal, "3", 0, 10),
        Config("Learned:MaxTotalPercent", "Largest total learned drift from the configured value (%)", SettingTier.Internal, "30", 1, 100),
        Config("Learned:MaxStepPercent", "Largest automatic learned step (% of the value)", SettingTier.Internal, "10", 1, 25),
        Config("Migrate:SkipOwner", "Skip owner provisioning at migrate", SettingTier.Internal, "false"),
        Config("Planning:Builder:UseSelection", "Plan builder uses the selection", SettingTier.Internal, "false"),
        Config("Planning:OpenBook:AssumedEmpty", "Assume the open book is empty", SettingTier.Internal, "false"),
        Config("Planning:OpenBook:UsePaperLedger", "Use the paper ledger as the open book", SettingTier.Internal, "false"),
        Config("Planning:Grading:CostSheetId", "Cost sheet for grading", SettingTier.Internal, null),
        Config("Planning:Grading:Capital", "Capital for grading", SettingTier.Internal, null),
    ];

    /// <summary>One Internal descriptor per configuration section bound at start-up, except <c>Confidence</c> (registered key by key). The value of a section is never shown.</summary>
    public static IReadOnlyList<SettingDescriptor> Sections { get; } =
    [
        .. new[]
        {
            "Live", "Live:Buckets", "Live:Estimates", "Live:Capacity", "Live:Report", "Live:Ramp", "Live:Capture", "Live:Match", "Live:Protect",
            "Ticks", "Depth", "Options", "Options:Liquidity", "Options:Costs", "Options:PhaseCheck", "Options:Remark", "Options:Quality",
            "Pairs", "Models", "Filings", "StrategyLab", "Experiments", "Recovery", "Shadow", "Heartbeat", "Exports", "Mcp", "Eval",
            "Learned", "Planning:TicketAlerts", "Reports:PlanVsActual", "Reports:Benchmark",
            "Portfolio:Heal", "Portfolio:BrokerHistory", "Portfolio:AutoRefresh", "Instruments:AutoRefresh", "Data", "Data:History",
            "Assistant:Mcp", "Assistant:Llm", "Assistant:Briefing", "Assistant:Turn", "Assistant:Proposals", "Assistant:Drafts", "AiRoles",
            "Brokers", "BrokerVault", "ExecHandover", "ExecRole", "Execution", "Execution:Sandbox", "Database", "Auth:Owner",
        }.Select(Section),
    ];

    /// <summary>Every descriptor of this registry.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } = [.. StoredSettings, .. ConfigurationSettings, .. Sections];
}
