using System.Globalization;
using System.Text;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Paper;

/// <summary>Which way a change of one setting leaves the risk (spec P10-05).</summary>
public enum ConfigDirection
{
    /// <summary>Any change restarts the count.</summary>
    Any,

    /// <summary>A lower number is the stricter setting (a limit).</summary>
    LowerStricter,

    /// <summary>A higher number is the stricter setting (a minimum).</summary>
    HigherStricter,

    /// <summary>Switching it on adds risk or trades; switching it off removes them.</summary>
    OnIsRisk,

    /// <summary>Switching it on protects; switching it off or loosening it adds risk.</summary>
    OnIsProtective,
}

/// <summary>
/// The pinned classification of every configuration key (spec P10-05, ADR 0119). A key that is not in the tables is <b>frozen</b> (the safe side): a new setting can never arrive unclassified and silently free. The property tables name every setting of
/// the typed settings; a reflection test fails when a property of those types is in neither a table nor <see cref="Folded"/>.
/// </summary>
public static class PaperConfigKeys
{
    /// <summary>Prefix of one eligible cell: <c>cells.eligible/&lt;cell&gt;</c>, value the strategy hash.</summary>
    public const string CellsEligible = "cells.eligible/";

    /// <summary>The cell-statistics snapshot in force.</summary>
    public const string CellsSnapshot = "cells.snapshot";

    /// <summary>Prefix of every <c>Planning:*</c> setting (flattened, lower-cased section path).</summary>
    public const string PlanningPrefix = "planning.";

    /// <summary>Prefix of the autonomy settings.</summary>
    public const string AutonomyPrefix = "autonomy.";

    /// <summary>The newest autonomy row hash: it changes with every save, so it is shown but never restarts the count.</summary>
    public const string AutonomyRowHash = "autonomy.row_hash";

    /// <summary>Prefix of a paper account's facts: <c>accounts.paper/&lt;id&gt;/&lt;field&gt;</c>.</summary>
    public const string AccountsPrefix = "accounts.paper/";

    /// <summary>Prefix of the smart-rule settings.</summary>
    public const string SmartRulesPrefix = "smart_rules.";

    /// <summary>Prefix of the selection rules.</summary>
    public const string SelectionPrefix = "selection.rules.";

    /// <summary>The registry champion.</summary>
    public const string ModelsChampion = "models.champion";

    /// <summary>Prefix of the engine facts.</summary>
    public const string EnginePrefix = "engine.";

    /// <summary>Services that write learning state (cell statistics, models, learning) by id. Enabled during an epoch, each run restarts the count.</summary>
    public static IReadOnlyList<string> LearningWriterServices { get; } = ["cells", "retrain", "weekly"];

    /// <summary>The warning shown beside an enabled learning writer.</summary>
    public const string BlockerText = "enabled during an epoch: each run restarts the count";

    /// <summary>The direction of each <see cref="TradingSettings"/> property that becomes a key.</summary>
    public static IReadOnlyDictionary<string, ConfigDirection> TradingFields { get; } = new Dictionary<string, ConfigDirection>
    {
        [nameof(TradingSettings.Enabled)] = ConfigDirection.OnIsRisk,
        [nameof(TradingSettings.Mode)] = ConfigDirection.Any,
        [nameof(TradingSettings.RiskProfileId)] = ConfigDirection.Any,
        [nameof(TradingSettings.CapitalSource)] = ConfigDirection.Any,
        [nameof(TradingSettings.CapitalAmount)] = ConfigDirection.Any,
        [nameof(TradingSettings.Intraday)] = ConfigDirection.OnIsRisk,
        [nameof(TradingSettings.Swing)] = ConfigDirection.OnIsRisk,
        [nameof(TradingSettings.IntradayInstruments)] = ConfigDirection.Any,
        [nameof(TradingSettings.SwingBuyInstruments)] = ConfigDirection.Any,
        [nameof(TradingSettings.SwingSellInstruments)] = ConfigDirection.Any,
    };

    /// <summary>The direction of each <see cref="RiskLimits"/> property. They are keyed by the effective value (an override, else the profile's).</summary>
    public static IReadOnlyDictionary<string, ConfigDirection> RiskLimitFields { get; } = new Dictionary<string, ConfigDirection>
    {
        [nameof(RiskLimits.RiskPerTradePct)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxCapitalPerTradePct)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxNewEntriesPerDay)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxOpenPositions)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxTotalRiskPct)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxPerSector)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxPairCorrelation)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxIntraday)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.MaxSwing)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.DailyLossR)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.DailyLossPct)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.WeeklyLossR)] = ConfigDirection.LowerStricter,
        [nameof(RiskLimits.WeeklyLossAction)] = ConfigDirection.Any,
    };

    /// <summary>The direction of each <see cref="SmartRulesOptions"/> property (the code defaults today: the settings home is unverified).</summary>
    public static IReadOnlyDictionary<string, ConfigDirection> SmartRuleFields { get; } = new Dictionary<string, ConfigDirection>
    {
        [nameof(SmartRulesOptions.GateEnabled)] = ConfigDirection.OnIsProtective,
        [nameof(SmartRulesOptions.VixReduce)] = ConfigDirection.LowerStricter,
        [nameof(SmartRulesOptions.VixNoTrade)] = ConfigDirection.LowerStricter,
        [nameof(SmartRulesOptions.BreadthMin)] = ConfigDirection.HigherStricter,
        [nameof(SmartRulesOptions.ReducedItemsFactor)] = ConfigDirection.LowerStricter,
        [nameof(SmartRulesOptions.ReducedRiskMultiplier)] = ConfigDirection.LowerStricter,
        [nameof(SmartRulesOptions.ThrottleEnabled)] = ConfigDirection.OnIsProtective,
        [nameof(SmartRulesOptions.ThrottleWindowTrades)] = ConfigDirection.Any,
        [nameof(SmartRulesOptions.ThrottleCut)] = ConfigDirection.HigherStricter,
        [nameof(SmartRulesOptions.CorrelationEnabled)] = ConfigDirection.OnIsProtective,
        [nameof(SmartRulesOptions.CorrelationMax)] = ConfigDirection.LowerStricter,
        [nameof(SmartRulesOptions.HonestyFraction)] = ConfigDirection.Any,
        [nameof(SmartRulesOptions.GateMaxAgeDays)] = ConfigDirection.Any,
    };

    /// <summary>The direction of each <see cref="AutonomySettings"/> property that becomes a key.</summary>
    public static IReadOnlyDictionary<string, ConfigDirection> AutonomyFields { get; } = new Dictionary<string, ConfigDirection>
    {
        [nameof(AutonomySettings.AutoApproveTickets)] = ConfigDirection.OnIsRisk,
        [nameof(AutonomySettings.PromoteFromReserve)] = ConfigDirection.OnIsRisk,
        [nameof(AutonomySettings.MinPWin)] = ConfigDirection.HigherStricter,
        [nameof(AutonomySettings.MaxAutoPerDay)] = ConfigDirection.LowerStricter,
        [nameof(AutonomySettings.Hash)] = ConfigDirection.Any,
    };

    /// <summary>Properties that are in no key, each with the reason (the explicit <c>free</c> allow-list of the reflection test).</summary>
    public static IReadOnlyDictionary<string, string> Folded { get; } = new Dictionary<string, string>
    {
        [nameof(AutonomySettings.Id)] = "the row id (and any entity id); the row hash covers the content and is shown as free",
        [nameof(TradingSettings.Overrides)] = "the composite of the four Override properties, which are folded into the effective limit keys",
        [nameof(TradingSettings.OverrideRiskPerTradePct)] = "folded into the effective risk_per_trade_pct key",
        [nameof(TradingSettings.OverrideMaxNewEntriesPerDay)] = "folded into the effective max_new_entries_per_day key",
        [nameof(TradingSettings.OverrideMaxOpenPositions)] = "folded into the effective max_open_positions key",
        [nameof(TradingSettings.OverrideDailyLossPct)] = "folded into the effective daily_loss_pct key",
        [nameof(RiskProfile.Name)] = "a label; the limits and the profile id are in the keys",
        [nameof(RiskProfile.IsSeeded)] = "bookkeeping",
        [nameof(RiskProfile.CreatedAt)] = "bookkeeping",
        [nameof(RiskProfile.UpdatedAt)] = "bookkeeping",
        [nameof(RiskProfile.Limits)] = "the composite of the limit properties, which are in the keys",
    };

    /// <summary>Property name to key part: <c>MaxAutoPerDay</c> becomes <c>max_auto_per_day</c>, <c>MinPWin</c> becomes <c>min_p_win</c>.</summary>
    public static string Snake(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>The class of a change of one key. <paramref name="before"/> is null for an added key, <paramref name="after"/> for a removed one. An unknown key is frozen.</summary>
    public static ConfigClass Classify(string key, string? before, string? after)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == AutonomyRowHash)
        {
            return ConfigClass.Free;
        }

        if (key.StartsWith(CellsEligible, StringComparison.Ordinal))
        {
            return after is null ? ConfigClass.RiskDown : ConfigClass.Frozen;
        }

        if (key.StartsWith(AutonomyPrefix, StringComparison.Ordinal))
        {
            return ByField(key[AutonomyPrefix.Length..], AutonomyFields, before, after);
        }

        if (key.StartsWith(SmartRulesPrefix, StringComparison.Ordinal))
        {
            return ByField(key[SmartRulesPrefix.Length..], SmartRuleFields, before, after);
        }

        if (key.StartsWith(AccountsPrefix, StringComparison.Ordinal))
        {
            var field = key[(key.LastIndexOf('/') + 1)..];
            var table = TradingFields.Concat(RiskLimitFields).FirstOrDefault(p => Snake(p.Key) == field);
            return table.Key is null || before is null || after is null ? ConfigClass.Frozen : Direct(table.Value, before, after);
        }

        return ConfigClass.Frozen;
    }

    /// <summary>What a change of the key does to the count, in words (for the preview).</summary>
    public static string Describe(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == AutonomyRowHash)
        {
            return "free: shown, never restarts the count";
        }

        if (key.StartsWith(CellsEligible, StringComparison.Ordinal))
        {
            return "frozen when added or its strategy changes; risk_down when removed (paused or retired)";
        }

        var direction = key switch
        {
            _ when key.StartsWith(AutonomyPrefix, StringComparison.Ordinal) => Lookup(AutonomyFields, key[AutonomyPrefix.Length..]),
            _ when key.StartsWith(SmartRulesPrefix, StringComparison.Ordinal) => Lookup(SmartRuleFields, key[SmartRulesPrefix.Length..]),
            _ when key.StartsWith(AccountsPrefix, StringComparison.Ordinal) => Lookup(TradingFields.Concat(RiskLimitFields), key[(key.LastIndexOf('/') + 1)..]),
            _ => ConfigDirection.Any,
        };
        return direction switch
        {
            ConfigDirection.LowerStricter => "lowering is risk_down, raising is frozen",
            ConfigDirection.HigherStricter => "raising is risk_down, lowering is frozen",
            ConfigDirection.OnIsRisk => "switching off is risk_down, switching on is frozen",
            ConfigDirection.OnIsProtective => "switching on is risk_down, switching off is frozen",
            _ => "frozen: any change restarts the count",
        };
    }

    private static ConfigDirection Lookup(IEnumerable<KeyValuePair<string, ConfigDirection>> table, string field) =>
        table.FirstOrDefault(p => Snake(p.Key) == field) is { Key: not null } found ? found.Value : ConfigDirection.Any;

    private static ConfigClass ByField(string field, IReadOnlyDictionary<string, ConfigDirection> table, string? before, string? after)
    {
        var entry = table.FirstOrDefault(p => Snake(p.Key) == field);
        return entry.Key is null || before is null || after is null ? ConfigClass.Frozen : Direct(entry.Value, before, after);
    }

    private static ConfigClass Direct(ConfigDirection direction, string before, string after)
    {
        switch (direction)
        {
            case ConfigDirection.OnIsRisk:
                return IsOn(before) && !IsOn(after) ? ConfigClass.RiskDown : ConfigClass.Frozen;
            case ConfigDirection.OnIsProtective:
                return !IsOn(before) && IsOn(after) ? ConfigClass.RiskDown : ConfigClass.Frozen;
            case ConfigDirection.LowerStricter or ConfigDirection.HigherStricter:
                if (!decimal.TryParse(before, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) || !decimal.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                {
                    return ConfigClass.Frozen;
                }

                var stricter = direction == ConfigDirection.LowerStricter ? a < b : a > b;
                return stricter ? ConfigClass.RiskDown : ConfigClass.Frozen;
            default:
                return ConfigClass.Frozen;
        }
    }

    private static bool IsOn(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
