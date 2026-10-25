using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Research;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Paper;

/// <summary>
/// Reads the configuration the paper count is measured against through the existing ports (spec P10-05, ADR 0119): about ten reads, once per capture run, no replay. It writes nothing. A group whose port fails is reported as unreadable (the snapshot is then
/// partial), never guessed. Only the engine and schema versions are in the fingerprint, not the code revision (Decision 4).
/// </summary>
internal sealed class ConfigSnapshotSource(
    IPlanningSettingsReader planning,
    IAutonomySettingsStore autonomy,
    IAccountStore accounts,
    IRiskProfileStore profiles,
    ILearningChangelog changelog,
    IStrategyStore strategies,
    ICellStatsStore cellStats,
    IModelRegistry models,
    ISimulationEngine engine,
    IGradeSettings grading,
    ICostSheetStore costSheets) : IConfigSnapshotSource
{
    private static readonly CellState[] Eligible = [CellState.Candidate, CellState.Paper, CellState.Active];

    public async ValueTask<ConfigReading> ReadAsync(CancellationToken cancellationToken)
    {
        var components = new List<ConfigComponent>();
        var unreadable = new List<string>();
        await Group("planning", () => Task.FromResult(Planning()), components, unreadable);
        await Group("autonomy", () => AutonomyAsync(cancellationToken), components, unreadable);
        await Group("accounts", () => AccountsAsync(cancellationToken), components, unreadable);
        await Group("cells", () => CellsAsync(cancellationToken), components, unreadable);
        await Group("models", () => ModelsAsync(cancellationToken), components, unreadable);
        await Group("engine", () => EngineAsync(cancellationToken), components, unreadable);
        components.AddRange(Rules(SelectionPrefix, new SelectionRules()));
        components.AddRange(SmartRules(new SmartRulesOptions()));
        return new ConfigReading(components, unreadable);
    }

    private static string SelectionPrefix => PaperConfigKeys.SelectionPrefix;

    private static async Task Group(string name, Func<Task<List<ConfigComponent>>> read, List<ConfigComponent> into, List<string> unreadable)
    {
        try
        {
            into.AddRange(await read());
        }
#pragma warning disable CA1031 // A port that fails makes the snapshot partial; cancellation is rethrown by the filter. The message is not kept (it can carry values).
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            unreadable.Add(name);
        }
    }

    private List<ConfigComponent> Planning() =>
        [.. planning.Read().Select(p => new ConfigComponent(PaperConfigKeys.PlanningPrefix + p.Key, p.Value))];

    private async Task<List<ConfigComponent>> AutonomyAsync(CancellationToken cancellationToken)
    {
        var s = await autonomy.CurrentAsync(cancellationToken);
        string P(string name) => PaperConfigKeys.AutonomyPrefix + PaperConfigKeys.Snake(name);
        return
        [
            new(P(nameof(s.AutoApproveTickets)), Bool(s.AutoApproveTickets)),
            new(P(nameof(s.PromoteFromReserve)), Bool(s.PromoteFromReserve)),
            new(P(nameof(s.MinPWin)), s.MinPWin.ToString("R", CultureInfo.InvariantCulture)),
            new(P(nameof(s.MaxAutoPerDay)), s.MaxAutoPerDay.ToString(CultureInfo.InvariantCulture)),
            new(PaperConfigKeys.AutonomyRowHash, s.Hash ?? "none"),
        ];
    }

    private async Task<List<ConfigComponent>> AccountsAsync(CancellationToken cancellationToken)
    {
        var profileById = (await profiles.ListAsync(cancellationToken)).ToDictionary(p => p.Id);
        var result = new List<ConfigComponent>();
        foreach (var account in (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading.Mode == TradingMode.Paper).OrderBy(a => a.Id))
        {
            var t = account.Trading;
            var prefix = PaperConfigKeys.AccountsPrefix + account.Id.ToString("N", CultureInfo.InvariantCulture) + "/";
            void Add(string property, object? value) => result.Add(new ConfigComponent(prefix + PaperConfigKeys.Snake(property), Text(value)));
            Add(nameof(t.Enabled), t.Enabled);
            Add(nameof(t.Mode), t.Mode);
            Add(nameof(t.RiskProfileId), t.RiskProfileId);
            Add(nameof(t.CapitalSource), t.CapitalSource);
            Add(nameof(t.CapitalAmount), t.CapitalAmount);
            Add(nameof(t.Intraday), t.Intraday);
            Add(nameof(t.Swing), t.Swing);
            Add(nameof(t.IntradayInstruments), t.IntradayInstruments);
            Add(nameof(t.SwingBuyInstruments), t.SwingBuyInstruments);
            Add(nameof(t.SwingSellInstruments), t.SwingSellInstruments);
            var profile = t.RiskProfileId is { } id && profileById.TryGetValue(id, out var p) ? p.Limits : null;
            var o = t.Overrides;
            Add(nameof(RiskLimits.RiskPerTradePct), o.RiskPerTradePct ?? profile?.RiskPerTradePct);
            Add(nameof(RiskLimits.MaxCapitalPerTradePct), profile?.MaxCapitalPerTradePct);
            Add(nameof(RiskLimits.MaxNewEntriesPerDay), o.MaxNewEntriesPerDay ?? profile?.MaxNewEntriesPerDay);
            Add(nameof(RiskLimits.MaxOpenPositions), o.MaxOpenPositions ?? profile?.MaxOpenPositions);
            Add(nameof(RiskLimits.MaxTotalRiskPct), profile?.MaxTotalRiskPct);
            Add(nameof(RiskLimits.MaxPerSector), profile?.MaxPerSector);
            Add(nameof(RiskLimits.MaxPairCorrelation), profile?.MaxPairCorrelation);
            Add(nameof(RiskLimits.MaxIntraday), profile?.MaxIntraday);
            Add(nameof(RiskLimits.MaxSwing), profile?.MaxSwing);
            Add(nameof(RiskLimits.DailyLossR), profile?.DailyLossR);
            Add(nameof(RiskLimits.DailyLossPct), o.DailyLossPct ?? profile?.DailyLossPct);
            Add(nameof(RiskLimits.WeeklyLossR), profile?.WeeklyLossR);
            Add(nameof(RiskLimits.WeeklyLossAction), profile?.WeeklyLossAction);
        }

        return result;
    }

    private async Task<List<ConfigComponent>> CellsAsync(CancellationToken cancellationToken)
    {
        var minimum = new SelectionRules().MinState;
        var rows = await changelog.ListAsync("cell", null, DigestBuilder.ReadLimit, cancellationToken);
        var result = new List<ConfigComponent>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var subject in rows.Select(r => r.Entry.Subject).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var replay = CellHistory.Replay(await changelog.HistoryAsync(subject, cancellationToken));
            if (!Eligible.Contains(replay.State) || replay.State < minimum || CellId.TryParse(subject) is not { } cell)
            {
                continue;
            }

            if (!hashes.TryGetValue(cell.Strategy, out var hash))
            {
                hash = (await strategies.GetVersionAsync(cell.Strategy, null, cancellationToken))?.Hash ?? "unknown";
                hashes[cell.Strategy] = hash;
            }

            result.Add(new ConfigComponent(PaperConfigKeys.CellsEligible + subject, hash));
        }

        var keys = (await cellStats.ListLatestAsync(null, null, cancellationToken)).Select(s => s.SnapshotKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        result.Add(new ConfigComponent(PaperConfigKeys.CellsSnapshot, string.Join(',', keys) is { Length: > 0 } joined ? joined : "none"));
        return result;
    }

    private async Task<List<ConfigComponent>> ModelsAsync(CancellationToken cancellationToken)
    {
        var champions = await models.ListAsync(ModelRole.Champion, 10, cancellationToken);
        return [new(PaperConfigKeys.ModelsChampion, champions.Count == 0 ? "none" : string.Join(',', champions.Select(c => c.Id).Order()))];
    }

    private async Task<List<ConfigComponent>> EngineAsync(CancellationToken cancellationToken)
    {
        var sheet = "none";
        if (grading.CostSheetId is { } id)
        {
            var found = await costSheets.FindAsync(id, cancellationToken);
            sheet = found.IsSuccess ? id + "@" + found.Value.RatesSha256 : id + "@not stored";
        }

        return
        [
            new(PaperConfigKeys.EnginePrefix + "version", engine.EngineVersion),
            new(PaperConfigKeys.EnginePrefix + "trade_schema", SimTrade.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)),
            new(PaperConfigKeys.EnginePrefix + "fill_rules", Sha(JsonSerializer.Serialize(FillRules.Default))),
            new(PaperConfigKeys.EnginePrefix + "charge_sheet", sheet),
            new(PaperConfigKeys.EnginePrefix + "capital", grading.Capital.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    private static IEnumerable<ConfigComponent> SmartRules(SmartRulesOptions options) =>
        Properties(options).Select(p => new ConfigComponent(PaperConfigKeys.SmartRulesPrefix + PaperConfigKeys.Snake(p.Name), Text(p.GetValue(options))));

    private static IEnumerable<ConfigComponent> Rules<T>(string prefix, T rules) where T : notnull =>
        Properties(rules).Select(p => new ConfigComponent(prefix + PaperConfigKeys.Snake(p.Name), Text(p.GetValue(rules))));

    private static IEnumerable<PropertyInfo> Properties(object value) =>
        value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.GetMethod?.IsStatic == false && p.Name is not ("Optional" or "EqualityContract"));

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Text(object? value) => value switch
    {
        null => "unset",
        bool b => Bool(b),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable e and not string => string.Join(',', e.Cast<object?>().Select(Text)),
        _ => value.ToString() ?? "unset",
    };

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
