using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// Posts a Lab run (spec P5-02, D2 and D3): checks the input, resolves the strategy version, the universe and the window now (a basket that changes later does not change the run), applies the quick-test sample, makes the key and queues the run, or
/// returns the run that already holds the key. Nothing is executed here; a queued run is picked up by the dispatcher.
/// </summary>
internal sealed class StartLabRunHandler(
    ILabRunStore runs,
    ITestLedger ledger,
    IStrategyStore strategies,
    IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> resolver,
    IBasketQueries baskets,
    IBarQueries bars,
    ISimulationEngine engine,
    ICostSheetStore sheets,
    LabSettings settings,
    TimeProvider time,
    ILogger<StartLabRunHandler> logger,
    LabPreregistration? preregistration = null,
    LabHoldOutGuard? holdOut = null,
    CostStressOptions? costStress = null,
    NullOptions? nullOptions = null,
    PlateauOptions? plateauOptions = null,
    IUniverseSettingsStore? universeSettings = null) : ICommandHandler<StartLabRunCommand, StartedLabRun>
{
    public async ValueTask<Result<StartedLabRun>> HandleAsync(StartLabRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var input = CheckInput(command);
        if (input is not null)
        {
            return Result.Failure<StartedLabRun>(input);
        }

        var version = await strategies.GetVersionAsync(command.StrategyId!, command.Version, cancellationToken);
        if (version is null)
        {
            return Result.Failure<StartedLabRun>(LabErrors.NotFound(command.StrategyId!, command.Version));
        }

        var full = LabSettings.RunsFullTest(command.Method);
        if (full && preregistration is not null && await preregistration.CheckAsync(version, cancellationToken) is { } notRegistered)
        {
            return Result.Failure<StartedLabRun>(notRegistered);
        }

        var universe = await ResolveUniverseAsync(command, full, cancellationToken);
        if (universe.IsFailure)
        {
            return Result.Failure<StartedLabRun>(universe.Error);
        }

        var window = await ResolveWindowAsync(command, full, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<StartedLabRun>(window.Error);
        }

        var (from, to, firstSession, lastSession) = window.Value;
        var dated = await ApplyMembersBasisAsync(command, universe.Value, full, from, to, cancellationToken);
        if (dated.IsFailure)
        {
            return Result.Failure<StartedLabRun>(dated.Error);
        }

        universe = dated;
        string? holdOutNote = null;
        if (holdOut is not null && await holdOut.EnsureAsync(firstSession, lastSession, cancellationToken) is { } block)
        {
            // CF-06: the block is fixed at the first post and no default Lab window reads it, whatever the setting; enforcement only refuses an explicit window that reaches it
            var cut = LabHoldOutGuard.Cut(block, from, to, command.To is not null, holdOut.Enforce);
            if (cut.IsFailure)
            {
                return Result.Failure<StartedLabRun>(cut.Error);
            }

            (to, holdOutNote) = cut.Value;
        }

        var (basis, basisLabel) = await CostBasisAsync(from, cancellationToken);
        if (full && basis == "none")
        {
            return Result.Failure<StartedLabRun>(LabErrors.InvalidInput($"A full test is costed: the charge sheet `{settings.CostSheetId}` is not stored or not in force on the first day of the window."));
        }

        var robust = string.Equals(command.Method, LabSettings.RobustMethod, StringComparison.Ordinal);
        var method = robust ? LabSettings.RobustMethod : full ? LabSettings.FullMethod : LabSettings.QuickMethod;
        // CF-07, ADR 0231: `nulls: true` and a `draws` equal to the configured number are the default and are not stored; only a real difference changes the key
        var configuredDraws = (nullOptions ?? new NullOptions()).Draws;
        var options = new LabRunOptions(string.Equals(command.Slippage, "conservative", StringComparison.Ordinal), command.Stress, null, command.Nulls == false, command.Draws is { } asked && asked != configuredDraws ? asked : null);
        var keyOptions = options.KeyText(robust);
        // CF-05, ADR 0230: a setting that changes what a run computes enters the key when it is used and differs from its default, so changing it cannot reuse an old result
        var stress = costStress ?? new CostStressOptions();
        var settingsKey = new List<string>(2);
        if (options.ConservativeSlippage && (stress.TierABps != TieredSlippage.TierABps || stress.TierBBps != TieredSlippage.TierBBps || stress.RangeFraction != TieredSlippage.BarRangeFrac))
        {
            settingsKey.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"tiers={stress.TierABps:0.####}/{stress.TierBBps:0.####}/{stress.RangeFraction:0.####}"));
        }

        if (engine.Describe(version.SourceText) is { IsSuccess: true, Value.ReadsTurnover: true })
        {
            settingsKey.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"min_turnover={stress.MinTurnoverCrore:0.####}cr"));
        }

        keyOptions = settingsKey.Count == 0 ? keyOptions : string.Join(';', keyOptions is null ? settingsKey : [keyOptions, .. settingsKey]);
        // CF-05: the key changes only when an option differs from the method's default, so every existing run keeps its key
        var configHash = LabRunIdentity.Sha256((robust ? settings.RobustConfigText(basis) : full ? settings.FullConfigText(basis) : settings.ConfigText(basis)) + (keyOptions is null ? string.Empty : ";" + keyOptions));
        var key = LabRunIdentity.Key(version.Hash, universe.Value.Label, [.. universe.Value.Instruments.Select(i => i.Id)], from, to, method, configHash, engine.EngineVersion);
        var labels = full ? FullLabels(universe.Value, from, to, firstSession, lastSession, basisLabel, robust) : Labels(universe.Value, from, to, firstSession, lastSession, basisLabel);
        if (holdOutNote is not null)
        {
            labels = [.. labels, holdOutNote];
        }

        if (options.ConservativeSlippage)
        {
            labels = [.. labels, RunLabels.ConservativeSlippage];
        }

        if (options.Stress == false)
        {
            labels = [.. labels, RunLabels.StressOff];
        }

        if (options.NullsOff)
        {
            labels = [.. labels, RunLabels.NullsOff];
        }

        if (options.Draws is { } ownDraws)
        {
            labels = [.. labels, RunLabels.Draws(ownDraws)];
        }

        var now = time.GetUtcNow();
        var inserted = await runs.InsertOrGetAsync(
            new NewLabRun(Guid.NewGuid(), key, version.StrategyId, version.Version, version.Hash, universe.Value, from, to, method, configHash, engine.EngineVersion, labels, now, options.ToText()),
            cancellationToken);
        // one look at the family, counted whatever becomes of the run (spec P5-03, D1). Also for a run that already holds the key: the append is idempotent by run id, so a run whose first append failed is healed by the next post.
        var run = inserted.Run;
        // CF-06: the extra exit candidates of a hash's first full test are looks too
        var extra = full && preregistration is not null ? await preregistration.ExtraTrialsAsync(run.StrategyId, run.StrategyHash, cancellationToken) : 0;
        if (await ledger.AppendAsync(new LedgerRow(run.Id, run.Key, run.StrategyId, run.Version, run.StrategyHash, version.ParentHash, run.Universe.Label, run.From, run.To, run.Method, run.CreatedAt, "run", extra), cancellationToken))
        {
            LabRunLog.LedgerAppended(logger, run.Id, run.StrategyId, run.Version);
        }

        if (full && preregistration is not null)
        {
            await preregistration.FreezeAsync(run, version, cancellationToken);
        }

        if (inserted.Created)
        {
            LabRunLog.Queued(logger, inserted.Run.Id, version.StrategyId, version.Version, key[..12], universe.Value.Instruments.Count, universe.Value.Sampled);
        }

        return Result.Success(new StartedLabRun(LabRunView.Of(inserted.Run), inserted.Created));
    }

    private Error? CheckInput(StartLabRunCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.StrategyId))
        {
            return LabErrors.InvalidInput("`strategyId` is required.");
        }

        if (!string.Equals(command.Method, LabSettings.QuickMethod, StringComparison.Ordinal) && !LabSettings.RunsFullTest(command.Method))
        {
            return LabErrors.InvalidInput("`method` must be `quick`, `full` or `robust`; the other methods are later slices.");
        }

        if (command.Slippage is not null && !string.Equals(command.Slippage, "default", StringComparison.Ordinal) && !string.Equals(command.Slippage, "conservative", StringComparison.Ordinal))
        {
            return LabErrors.InvalidInput("`slippage` must be `default` or `conservative`.");
        }

        if (command.Stress == true && !LabSettings.RunsFullTest(command.Method))
        {
            return LabErrors.InvalidInput("`stress` needs the out-of-sample trades of a `full` or `robust` run; a `quick` run has none.");
        }

        if (command.Stress == false && string.Equals(command.Method, LabSettings.RobustMethod, StringComparison.Ordinal) && costStress is { IsOn: true, Enforce: true })
        {
            return LabErrors.InvalidInput("`stress: false` is refused on a `robust` run while the cost stress is enforced (Confidence:CostStress:Enforce): it would skip the cap the gates apply.");
        }

        if ((command.Nulls is not null || command.Draws is not null) && !string.Equals(command.Method, LabSettings.RobustMethod, StringComparison.Ordinal))
        {
            return LabErrors.InvalidInput("`nulls` and `draws` belong to the chance and plateau block of a `robust` run.");
        }

        if (command.Nulls == false && command.Draws is not null)
        {
            return LabErrors.InvalidInput("`draws` has no meaning with `nulls: false`.");
        }

        var nullCfg = nullOptions ?? new NullOptions();
        if (command.Draws is { } draws && (draws < NullOptions.MinRequestDraws || draws > nullCfg.MaxDraws))
        {
            return LabErrors.InvalidInput($"`draws` is {NullOptions.MinRequestDraws.ToString(CultureInfo.InvariantCulture)} to {nullCfg.MaxDraws.ToString(CultureInfo.InvariantCulture)} (Confidence:Nulls:MaxDraws).");
        }

        if (command.Nulls == false && nullCfg is { IsOn: true, Enforce: true })
        {
            return LabErrors.InvalidInput("`nulls: false` is refused on a `robust` run while the nulls are enforced (Confidence:Nulls:Enforce): it would skip the cap the gates apply.");
        }

        if (command.Nulls == false && plateauOptions is { IsOn: true, Enforce: true })
        {
            return LabErrors.InvalidInput("`nulls: false` is refused on a `robust` run while the plateau gate is enforced (Confidence:Plateau:Enforce): the gate lives in the same block and leaving it out would skip the cap.");
        }

        if (command.Draws is not null && !nullCfg.IsOn)
        {
            return LabErrors.InvalidInput("`draws` is refused while the nulls are switched off (Confidence:Nulls:Mode).");
        }

        if (command.Version is < 1)
        {
            return LabErrors.InvalidInput("`version` is 1 or more.");
        }

        if ((command.BasketId is null) == (command.Symbols is null))
        {
            return LabErrors.InvalidInput("Give exactly one of `universe.basketId` and `universe.symbols`.");
        }

        if (command.Symbols is { Count: 0 } or { Count: > 2000 })
        {
            return LabErrors.InvalidInput("`universe.symbols` has one to 2,000 symbols.");
        }

        return command.From is { } from && command.To is { } to && from > to
            ? LabErrors.InvalidInput("`window.from` is after `window.to`.")
            : (command.From is null) != (command.To is null) ? LabErrors.InvalidInput("Give both `window.from` and `window.to`, or neither.") : null;
    }

    private async ValueTask<Result<LabUniverse>> ResolveUniverseAsync(StartLabRunCommand command, bool full, CancellationToken cancellationToken)
    {
        List<LabInstrument> members;
        string kind;
        if (command.BasketId is { } basketId)
        {
            var basket = await baskets.MembersAsync(basketId, null, null, cancellationToken);
            if (basket is null || basket.ArchivedAt is not null)
            {
                return Result.Failure<LabUniverse>(LabErrors.BasketNotFound(basketId));
            }

            if (full && universeSettings is not null)
            {
                // BK-11 (ADR 0261): a full test of a strategy on a basket is a strategy x basket comparison; the basket must be one of the registered ones (the owner's setting), else the grid of strategies x baskets is the cheapest way to fool oneself
                var registered = (await universeSettings.CurrentAsync(cancellationToken)).Settings.RegisteredBaskets;
                if (!UniverseSettingsRules.IsRegistered(basket.Name, registered))
                {
                    return Result.Failure<LabUniverse>(LabErrors.UniverseNotRegistered(basket.Name, registered));
                }
            }

            kind = "basket";
            members = [.. basket.Items.OrderBy(m => m.Position).Select(m => new LabInstrument(m.Instrument.Id, m.Instrument.Symbol)).DistinctBy(i => i.Id)];
        }
        else
        {
            var resolved = await resolver.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput(command.Symbols)), cancellationToken);
            if (resolved.IsFailure)
            {
                return Result.Failure<LabUniverse>(LabErrors.InvalidInput(resolved.Error.Message));
            }

            if (resolved.Value.Unmatched.Count > 0)
            {
                var first = resolved.Value.Unmatched[0];
                return Result.Failure<LabUniverse>(LabErrors.InvalidInput($"{resolved.Value.Unmatched.Count} symbol(s) are not known; the first is `{Show(first.Term)}`: {first.Reason}"));
            }

            kind = "symbols";
            members = [.. resolved.Value.Matched.Select(m => new LabInstrument(m.Id, m.Symbol)).DistinctBy(i => i.Id)];
        }

        if (members.Count == 0)
        {
            return Result.Failure<LabUniverse>(LabErrors.InvalidInput("The universe has no instrument."));
        }

        var choice = QuickSample.Choose(members.Select(m => m.Id), full ? int.MaxValue : settings.QuickSymbols);
        var byId = members.ToDictionary(m => m.Id);
        var chosen = choice.Ids.Select(id => byId[id]).ToList();
        return Result.Success(new LabUniverse(kind, command.BasketId, kind == "basket" ? "basket members at the time of the post" : "symbols as listed", members.Count, chosen, choice.Sampled, choice.Seed));
    }

    /// <summary>
    /// BK-11b: the members basis of a basket run. Without one nothing changes. <c>dated</c> needs a full or robust test (the quick test samples a list), a basket with dated membership for the window start (else 422 <c>simulation.members_not_dated</c>, as the simulate path) and
    /// replaces the universe by the instruments that are a member on some day of the window, under the dated label, so the plan hash and the key differ from the snapshot test. <c>single_snapshot</c> is recorded and keeps the list and the label.
    /// </summary>
    private async ValueTask<Result<LabUniverse>> ApplyMembersBasisAsync(StartLabRunCommand command, LabUniverse universe, bool full, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (command.MembersBasis is not { } basis)
        {
            return Result.Success(universe);
        }

        if (command.BasketId is not { } basketId || basis is not (UniverseBasisRecord.Dated or UniverseBasisRecord.SingleSnapshot))
        {
            return Result.Failure<LabUniverse>(LabErrors.InvalidInput("`universe.membersBasis` is dated or single_snapshot and goes with `universe.basketId`."));
        }

        if (basis == UniverseBasisRecord.SingleSnapshot)
        {
            return Result.Success(universe with { MembersBasis = basis });
        }

        if (!full)
        {
            return Result.Failure<LabUniverse>(LabErrors.InvalidInput("The dated basis needs a full or robust test: the quick test samples the members of one day."));
        }

        var stays = await baskets.StaysAsync(basketId, cancellationToken);
        if (stays is null || stays.ArchivedAt is not null)
        {
            return Result.Failure<LabUniverse>(LabErrors.BasketNotFound(basketId));
        }

        var members = new DatedMembers(stays.Items.Select(s => (s.InstrumentId, s.ValidFrom, s.ValidTo)));
        if (!members.IsDatedFor(from, out var firstDay))
        {
            return Result.Failure<LabUniverse>(SimulationErrors.MembersNotDated(firstDay));
        }

        var chosen = stays.Items.Where(s => members.IsMemberDuring(s.InstrumentId, from, to)).DistinctBy(s => s.InstrumentId).OrderBy(s => s.Position).Select(s => new LabInstrument(s.InstrumentId, s.Symbol)).ToList();
        return chosen.Count == 0
            ? Result.Failure<LabUniverse>(LabErrors.InvalidInput("The universe has no instrument."))
            : Result.Success(universe with { Label = UniverseLabels.Dated(basketId, null), Requested = chosen.Count, Instruments = chosen, Sampled = false, Seed = null, MembersBasis = basis });
    }

    private static string Show(string term) => term.Length <= 40 ? term : term[..40];

    /// <summary>The window of the run and the first and last stored session: the posted window, or the last <c>QuickYears</c> of stored data ending at the last stored session (D2).</summary>
    private async ValueTask<Result<(DateOnly From, DateOnly To, DateOnly First, DateOnly Last)>> ResolveWindowAsync(StartLabRunCommand command, bool full, CancellationToken cancellationToken)
    {
        var totals = await bars.GetBarTotalsAsync(time.GetUtcNow(), cancellationToken);
        if (totals.FirstBar is not { } firstBar || totals.LastBar is not { } lastBar)
        {
            var from = command.From ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            return Result.Failure<(DateOnly, DateOnly, DateOnly, DateOnly)>(LabErrors.NoData(from, command.To ?? from));
        }

        var first = SessionClock.DateOf(firstBar.ToUnixTimeSeconds());
        var last = SessionClock.DateOf(lastBar.ToUnixTimeSeconds());
        var to = command.To ?? last;
        var start = command.From ?? (full ? first : Max(first, last.AddYears(-settings.QuickYears).AddDays(1)));
        return start > last || to < first
            ? Result.Failure<(DateOnly, DateOnly, DateOnly, DateOnly)>(LabErrors.NoData(start, to))
            : Result.Success((start, to, first, last));
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    /// <summary>The cost basis: the stored sheet of the settings when it is stored and in force on the first day of the window, otherwise gross (spec P5-02, question 4). The first part goes into the key; the second is the label.</summary>
    private async ValueTask<(string Basis, string Label)> CostBasisAsync(DateOnly from, CancellationToken cancellationToken)
    {
        var sheet = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
        return sheet.IsSuccess && sheet.Value.Schedules[0].EffectiveFrom <= from
            ? ($"{sheet.Value.Id}|{sheet.Value.RatesSha256}", $"Costed with the charge sheet `{sheet.Value.Id}` (capital {CostChoice.DefaultCapital.ToString("0", CultureInfo.InvariantCulture)}, profile {CostChoice.DefaultProfile}).")
            : ("none", $"Gross: no charges applied, because the charge sheet `{settings.CostSheetId}` is not stored or not in force on the first day of the window.");
    }

    private IReadOnlyList<string> FullLabels(LabUniverse universe, DateOnly from, DateOnly to, DateOnly firstSession, DateOnly lastSession, string basisLabel, bool robust) =>
    [
        "Full test: walk-forward out of sample. Only the out-of-sample numbers are judged; the full-period figures are exploration, in-sample.",
        basisLabel,
        $"Window {from:yyyy-MM-dd} to {to:yyyy-MM-dd} (all stored data unless posted).",
        $"All {universe.Instruments.Count} instruments (no sample).",
        $"Candidate exits: {string.Join(' ', settings.Exits)}; the strategy's own exit is not a candidate.",
        $"Stored data runs from {firstSession:yyyy-MM-dd} to {lastSession:yyyy-MM-dd}.",
        .. robust ? ["Robustness checks (sensitivity sweep, Monte Carlo, by year, sign-flip baseline) are exploration beside the verdict and do not change it; each sweep point is a variant in the ledger."] : Array.Empty<string>(),
    ];

    private IReadOnlyList<string> Labels(LabUniverse universe, DateOnly from, DateOnly to, DateOnly firstSession, DateOnly lastSession, string basisLabel)
    {
        var years = settings.QuickYears.ToString(CultureInfo.InvariantCulture);
        var sample = universe.Sampled
            ? $"Sample: {universe.Instruments.Count} of {universe.Requested} instruments, the first {universe.Instruments.Count} of a seeded shuffle of the sorted ids (seed {universe.Seed?.ToString(CultureInfo.InvariantCulture)}); not the most liquid."
            : $"All {universe.Instruments.Count} instruments.";
        return
        [
            "Quick test: exploration, not a verdict.",
            basisLabel,
            $"Window {from:yyyy-MM-dd} to {to:yyyy-MM-dd} (the last {years} year(s) of stored data unless posted); in-sample, no hold-out.",
            sample,
            $"Stored data runs from {firstSession:yyyy-MM-dd} to {lastSession:yyyy-MM-dd}; the first and last bar of each instrument are in its simulation record.",
        ];
    }
}
