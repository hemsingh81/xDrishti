using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Lab;

/// <summary>
/// Posts a Lab run (spec P5-02, D2 and D3): checks the input, resolves the strategy version, the universe and the window now (a basket that changes later does not change the run), applies the quick-test sample, makes the key and queues the run, or
/// returns the run that already holds the key. Nothing is executed here; a queued run is picked up by the dispatcher.
/// </summary>
internal sealed class StartLabRunHandler(
    ILabRunStore runs,
    IStrategyStore strategies,
    IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> resolver,
    IBasketQueries baskets,
    IBarQueries bars,
    ISimulationEngine engine,
    ICostSheetStore sheets,
    LabSettings settings,
    TimeProvider time,
    ILogger<StartLabRunHandler> logger) : ICommandHandler<StartLabRunCommand, StartedLabRun>
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

        var universe = await ResolveUniverseAsync(command, cancellationToken);
        if (universe.IsFailure)
        {
            return Result.Failure<StartedLabRun>(universe.Error);
        }

        var window = await ResolveWindowAsync(command, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<StartedLabRun>(window.Error);
        }

        var (from, to, firstSession, lastSession) = window.Value;
        var (basis, basisLabel) = await CostBasisAsync(from, cancellationToken);
        var configHash = LabRunIdentity.Sha256(settings.ConfigText(basis));
        var key = LabRunIdentity.Key(version.Hash, universe.Value.Label, [.. universe.Value.Instruments.Select(i => i.Id)], from, to, LabSettings.QuickMethod, configHash, engine.EngineVersion);
        var labels = Labels(universe.Value, from, to, firstSession, lastSession, basisLabel);
        var now = time.GetUtcNow();
        var inserted = await runs.InsertOrGetAsync(
            new NewLabRun(Guid.NewGuid(), key, version.StrategyId, version.Version, version.Hash, universe.Value, from, to, LabSettings.QuickMethod, configHash, engine.EngineVersion, labels, now),
            cancellationToken);
        if (inserted.Created)
        {
            LabRunLog.Queued(logger, inserted.Run.Id, version.StrategyId, version.Version, key[..12], universe.Value.Instruments.Count, universe.Value.Sampled);
        }

        return Result.Success(new StartedLabRun(LabRunView.Of(inserted.Run), inserted.Created));
    }

    private static Error? CheckInput(StartLabRunCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.StrategyId))
        {
            return LabErrors.InvalidInput("`strategyId` is required.");
        }

        if (!string.Equals(command.Method, LabSettings.QuickMethod, StringComparison.Ordinal))
        {
            return LabErrors.InvalidInput("`method` must be `quick`; the other methods are later slices.");
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

    private async ValueTask<Result<LabUniverse>> ResolveUniverseAsync(StartLabRunCommand command, CancellationToken cancellationToken)
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

        var choice = QuickSample.Choose(members.Select(m => m.Id), settings.QuickSymbols);
        var byId = members.ToDictionary(m => m.Id);
        var chosen = choice.Ids.Select(id => byId[id]).ToList();
        return Result.Success(new LabUniverse(kind, command.BasketId, kind == "basket" ? "basket members at the time of the post" : "symbols as listed", members.Count, chosen, choice.Sampled, choice.Seed));
    }

    private static string Show(string term) => term.Length <= 40 ? term : term[..40];

    /// <summary>The window of the run and the first and last stored session: the posted window, or the last <c>QuickYears</c> of stored data ending at the last stored session (D2).</summary>
    private async ValueTask<Result<(DateOnly From, DateOnly To, DateOnly First, DateOnly Last)>> ResolveWindowAsync(StartLabRunCommand command, CancellationToken cancellationToken)
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
        var start = command.From ?? Max(first, last.AddYears(-settings.QuickYears).AddDays(1));
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
