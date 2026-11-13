using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
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
// shortcut: UNVERIFIED shapes are read leniently (the smart-rule home above; a strategy version's parameters are read by FingerprintEpochGate's VersionParameters).
internal sealed class ConfigSnapshotSource(
    IPlanningSettingsReader planning,
    IAutonomySettingsStore autonomy,
    IAccountStore accounts,
    IRiskProfileStore profiles,
    ILearningChangelog changelog,
    IStrategyStore strategies,
    ICellStatsStore cellStats,
    IRunStore runs,
    IModelRegistry models,
    ISimulationEngine engine,
    IGradeSettings grading,
    ICostSheetStore costSheets,
    IEventFilterDecisions? eventFilter = null,
    IListFilterSettings? listFilters = null,
    ILogger<ConfigSnapshotSource>? logger = null) : IConfigSnapshotSource
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
        if (eventFilter is not null)
        {
            // P15-06: the event-risk switch is a stored setting (its newest decision row); an unreadable history makes the snapshot partial, it is not read as OFF.
            await Group("event_filter", () => EventFilterAsync(eventFilter, cancellationToken), components, unreadable);
        }

        if (listFilters is not null)
        {
            components.Add(new(PaperConfigKeys.SmartRulesPrefix + PaperConfigKeys.ListFiltersEnabled, Bool(listFilters.Enabled))); // AM-01a: Selection:ListFilters:Enabled
        }

        return new ConfigReading(components, unreadable);
    }

    private static async Task<List<ConfigComponent>> EventFilterAsync(IEventFilterDecisions decisions, CancellationToken cancellationToken)
    {
        var state = XDrishti.Application.Selection.EventFilterSettings.StateOf(await decisions.RowsAsync(2, cancellationToken));
        return
        [
            new(PaperConfigKeys.SmartRulesPrefix + PaperConfigKeys.EventFilterEnabled, Bool(state.Enabled)),
            new(PaperConfigKeys.SmartRulesPrefix + PaperConfigKeys.EventFilterResultsWithinDays, state.ResultsWithinDays.ToString(CultureInfo.InvariantCulture)),
            new(PaperConfigKeys.SmartRulesPrefix + PaperConfigKeys.EventFilterApproval, state.Approval),
        ];
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


        // One read when the list is complete (fewer rows than the read limit): the histories are grouped from it. Only a list that may be cut at the limit falls back to one history read per cell.
        var complete = rows.Count < DigestBuilder.ReadLimit;
        var grouped = rows.GroupBy(r => r.Entry.Subject, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<ChangelogEntry>)[.. g.OrderBy(r => r.Id)], StringComparer.Ordinal);
        foreach (var subject in grouped.Keys.Order(StringComparer.Ordinal))
        {
            var replay = CellHistory.Replay(complete ? grouped[subject] : await changelog.HistoryAsync(subject, cancellationToken));
            if (!Eligible.Contains(replay.State) || replay.State < minimum || CellId.TryParse(subject) is not { IsCash: true } cell)
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

        var snapshots = CashCells.Only(await cellStats.ListLatestCashAsync(null, null, cancellationToken), "paper_config", logger);
        var keys = snapshots.Select(s => s.SnapshotKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        result.Add(new ConfigComponent(PaperConfigKeys.CellsSnapshot, string.Join(',', keys) is { Length: > 0 } joined ? joined : "none"));
        result.Add(new ConfigComponent(PaperConfigKeys.CellsBasis, await BasisAsync(snapshots, cancellationToken)));
        return result;
    }

    /// <summary>
    /// SHA-256 of the options and the sources of the newest cash snapshots without their window ends (ADR 0286). Each source's identity is built from its stored request (the replay baseline of the record), every field except the end and the resolved
    /// instrument ids: definition hash, parameters, timeframe, run data options, csv flag, fills, cost choice with capital and profile, exit and time stop, universe (the basket id or the label) and start, with the style and exit labels. A refresh only moves ends and keeps it.
    /// A source that cannot be read throws, so the group is reported as unreadable (the snapshot is then partial), never guessed.
    /// </summary>
    // shortcut: the regime and universe splits of the snapshot and the Confidence/Shrinkage options are not in the basis; add them to it once a gate or the selection reads them.
    private async Task<string> BasisAsync(IReadOnlyList<StoredCellStats> snapshots, CancellationToken cancellationToken)
    {
        if (snapshots.Count == 0)
        {
            return "none";
        }

        var identities = new Dictionary<string, string>(StringComparer.Ordinal); // one read per run key: the same run is often a source of several snapshots
        var parts = new List<string>();
        foreach (var snapshot in snapshots.GroupBy(s => s.SnapshotKey, StringComparer.Ordinal).Select(g => g.First()).OrderBy(s => s.SnapshotKey, StringComparer.Ordinal))
        {
            var o = snapshot.Options;
            var sources = CellSource.TryParseList(snapshot.Sources) ?? throw new InvalidOperationException("The snapshot's sources cannot be read.");
            var lines = new List<string>();
            foreach (var source in sources)
            {
                if (!identities.TryGetValue(source.RunKey, out var identity))
                {
                    identity = await SourceIdentityAsync(source.RunKey, cancellationToken);
                    identities[source.RunKey] = identity;
                }

                lines.Add(identity + "|" + source.Style + "|" + source.Exit);
            }

            lines.Sort(StringComparer.Ordinal);
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"opt:{o.RecencyHalfLifeDays:R}:{o.PriorStrength:R}:{o.MinTrades}:{o.CredibleLevel:R}") + "\n" + string.Join('\n', lines));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n--\n", parts))));
    }

    /// <summary>The sizing numbers of the stored profile (risk per trade and the capital cap), as written in the record: the profile name alone does not prove them. Throws when missing (fail closed).</summary>
    private static string ProfileNumbers(string record)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(record);
        var profile = doc.RootElement.GetProperty("costs").GetProperty("profile");
        return profile.GetProperty("risk_per_trade_pct").GetRawText() + "/" + profile.GetProperty("max_capital_per_trade_pct").GetRawText();
    }

    // shortcut: a record-schema bump (RecordSchemaVersion != CurrentSchemaVersion) makes the reader refuse the record, so the cells group is unreadable and the paper capture is blind until the sources are simulated again; the upgrade path is to read the identity fields schema-tolerantly.
    // shortcut: a dated (members-as-of) source is re-simulated by the refresh as a "members now" run because the baseline drops MembersBasis; the label in the basis shows that honestly as a frozen change. Follow-up: refuse dated sources in the refresh or carry MembersBasis.
    private async Task<string> SourceIdentityAsync(string runKey, CancellationToken cancellationToken)
    {
        var header = await runs.FindAsync(runKey, cancellationToken) ?? throw new InvalidOperationException("A source run is not stored.");
        var record = await runs.ReadRecordTextAsync(runKey, cancellationToken) ?? throw new InvalidOperationException("A source run's record is not stored.");
        var read = XDrishti.Application.Learning.Jobs.ReplayBaselineReader.Read(header, record);
        if (read.IsFailure)
        {
            throw new InvalidOperationException("A source run cannot be rebuilt from its record.");
        }

        var b = read.Value;
        var inv = CultureInfo.InvariantCulture;
        var costs = b.Costs is XDrishti.Application.Research.CostChoice.SheetChoice sheet
            ? string.Create(inv, $"sheet:{sheet.SheetId}:{sheet.Capital}:{sheet.Profile}:{sheet.MaxCostR}:{header.CostsRatesSha256}:{ProfileNumbers(record)}")
            : "gross";
        var exit = b.Exit?.Id ?? "none";
        var timeStop = b.TimeStop is { } t ? string.Create(inv, $"{t.MinProgressRr:R}/{t.IntradayBars}/{t.SwingSessions}") : "none";
        var parameters = string.Join(',', b.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => string.Create(inv, $"{p.Key}={p.Value:R}")));
        var universe = b.BasketId is { } basket
            ? "basket:" + basket.ToString("N") + ":" + header.UniverseLabel
            : "list:" + string.Join(',', b.InstrumentIds.Select(i => i.ToString("N")).Order(StringComparer.Ordinal)); // the ids of a list are the request itself, stable across refreshes
        return string.Join('|', b.DefinitionSha256, parameters, b.Timeframe, string.Create(inv, $"{b.Options.MinSessionCompleteness:R}/{b.Options.DiscontinuityThreshold:R}"), b.AcknowledgeCsvAdjusted ? "adj" : "raw", b.Fills.ToCanonicalJson(), costs, exit, timeStop, universe, b.From.ToString("yyyy-MM-dd", inv));
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

    // shortcut: the smart-rule and selection options have no settings home yet (the engine builds them with `new`), so their code defaults are read; a change arrives with a deploy and is seen then. Upgrade when they become stored settings (read the stored values here).
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
