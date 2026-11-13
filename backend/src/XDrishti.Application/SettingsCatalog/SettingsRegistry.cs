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
        Stored("sizing.vol_target.enabled", "Scale suggestions down in unusually volatile shares (owner only, password every change)", SettingTier.Advanced, "false"),
        Stored("sizing.vol_target.floor", "Lowest volatility scale, 0.25 to 1.0; raising it loosens (owner only, password every change)", SettingTier.Advanced, "0.5"),
        Stored("sizing.budget.min_fit", "Lowest share of its size a basket budget may cut a candidate to, 0.25 to 1.0; lowering it loosens (owner only, password every change)", SettingTier.Advanced, "0.5"),
        Stored("sizing.capacity.enabled", "Cap suggestions at a share of the 20-day traded value (owner only, password every change)", SettingTier.Advanced, "false"),
        Stored("sizing.capacity.pct", "Capacity share of the 20-day traded value, at most the configured capacity share 0.005: tighten only (owner only, password every change)", SettingTier.Advanced, "0.005"),
        Stored("sizing.basket_budgets", "Basket risk budgets, a share of the total open risk limit (owner only, password every change)", SettingTier.Advanced, null),
        Stored("gate.breadth_source", "Where the market gate reads breadth: tracked, or basket:<id>; any change loosens (owner only, password every change)", SettingTier.Advanced, "tracked"),
        Stored("selection.max_cluster_risk_pct", "Cap on open risk per correlation cluster, percent of capital; off when unset (owner only, password every change)", SettingTier.Advanced, null),
        Stored("selection.cluster_rho", "Correlation at which clusters are cut; raising it loosens (owner only, password every change)", SettingTier.Advanced, "0.7"),
        Stored("plans.loosening_banner.auto_dismiss", "Hide a loosened limit from the plan banner automatically once a plan has shown it (owner only, password every change)", SettingTier.Advanced, "false"),
        Stored("learning.universe_min_trades_child", "Fewest trades a liquidity-tier cell needs before it can be read in place of the all-shares cell (owner only, password every change)", SettingTier.Advanced, "30"),
        Stored("learning.universe_link_min_trades", "Fewest trades a liquidity tier needs before its edge is listed as an observation (owner only, password every change)", SettingTier.Advanced, "100"),
        Stored("learning.universe_registered_baskets", "Baskets a strategy x basket comparison may use, by name (owner only, password every change)", SettingTier.Advanced, "Nifty 50,Nifty 100,Nifty Midcap 150,F&O stocks,Most liquid,Low volatility,Momentum,Nifty 500"),
        Stored("learning.universe_link.auto_propose", "Propose each stable liquidity-tier observation as a hypothesis automatically, as the actor auto; off means nothing is proposed automatically (owner only, password every change)", SettingTier.Advanced, "false"),
        Stored("reports.basket_benchmark_instrument", "Index the basket profile and ranking compare with, an instrument id (unset: Nifty 500) (owner may change, no password: it loosens nothing)", SettingTier.Advanced, "Nifty 500"),
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
        Config("Simulate:Workers", "Simulate-everything: chunks run at once", SettingTier.Internal, "2", 1, 16),
        Config("Simulate:MinFreeGb", "Simulate-everything: free disk floor (GiB), above the recorders' floor", SettingTier.Internal, "40", 10, 500),
        Config("Simulate:RunInMarketHours", "Simulate-everything: also run in market hours", SettingTier.Internal, "false"),
        Config("Training:Enabled", "Train a challenger from a completed simulate-everything job", SettingTier.Internal, "false"),
        Config("Training:EmbargoSessions", "Training from the simulation: embargo before each test window (sessions)", SettingTier.Internal, "5", 0, 20),
        Config("Training:MinRows", "Training from the simulation: fewest closed trades to train on", SettingTier.Internal, "1000", 100, 1000000),
        Config("Training:MaxRows", "Training from the simulation: most rows held in memory, about 2 KiB each, inside the 1 GiB xd-api container (above it the rows are thinned)", SettingTier.Internal, "150000", 10000, 200000),
        Config("Replay:IncompleteDataShare", "System replay: share of the universe with incomplete bars above which a session takes no new trades", SettingTier.Internal, "0.2", 0, 1),
        Config("Replay:MinFreeGb", "System replay: free disk floor (GiB), as for the simulate-everything job", SettingTier.Internal, "40", 10, 500),
        Config("Replay:ReadAheadSessions", "System replay: sessions of bars read ahead in one chunk of the rolling window (bounds memory)", SettingTier.Internal, "40", 1, 250),
        Config("Replay:MaxWindowMb", "System replay: most memory the bars of one replay may need (MiB); raise it with the xd-api memory limit", SettingTier.Internal, "300", 64, 4096),
        Config("Replay:RunInMarketHours", "System replay: also run in market hours", SettingTier.Internal, "false"),
        Config("Simulate:ChunkTimeoutMinutes", "Simulate-everything: longest time of one chunk (minutes)", SettingTier.Internal, "30", 1, 240),
        Config("Learning:CellsRefresh:NoticeAfterSessions", "Cell statistics refresh: the bell speaks when the newest snapshot is this many sessions old (the nightly plan fails at 6)", SettingTier.Internal, "3", 1, 4),
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
            "OpeningBars", "Options", "Options:Liquidity", "Options:Costs", "Options:PhaseCheck", "Options:Remark", "Options:Quality",
            "Pairs", "Models", "Filings", "StrategyLab", "Experiments", "Recovery", "Shadow", "Heartbeat", "Exports", "Mcp", "Eval",
            "Learned", "Learning:CellsRefresh", "Simulate", "Training", "Replay", "Planning:TicketAlerts", "Reports:PlanVsActual", "Reports:Benchmark", "Reports:BasketProfile", "Reports:Training", "Market:RelativeStrength",
            "Portfolio:Heal", "Portfolio:BrokerHistory", "Portfolio:AutoRefresh", "Instruments:AutoRefresh", "Data", "Data:History", "Baskets:System:Fetch", "Baskets:System:Lists", "Baskets:System:Derived", "Selection:ListFilters",
            "Assistant:Mcp", "Assistant:Llm", "Assistant:Briefing", "Assistant:Turn", "Assistant:Proposals", "Assistant:Drafts", "AiRoles",
            "Brokers", "BrokerVault", "ExecHandover", "ExecRole", "Execution", "Execution:Sandbox", "Database", "Auth:Owner", "Api",
        }.Select(Section),
    ];

    /// <summary>Every descriptor of this registry.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } = [.. StoredSettings, .. ConfigurationSettings, .. Sections];
}
