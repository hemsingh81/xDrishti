using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Api.Simulate;

/// <summary>
/// What the verbs <c>simulate</c> and <c>verify-pack</c> share (spec P3-09, V4; ADR 0041 Amendment 4): resolve the universe and gate the tick unit, build the request from a strategy id, run the handler, read the cards a second time, and the console
/// helpers (usage, refusal, the lines of a refused run). Extracted from <see cref="SimulateCommand"/> with no change of behaviour: the order of the steps stays with each verb, and so do the files of its folder.
/// </summary>
internal static class SimulatePipeline
{
    /// <summary>The universe after the gate: refused with a code and its problems, or ready.</summary>
    public sealed record Gate(SimulateUniverse Universe, string? RefusalCode, IReadOnlyList<string> Problems)
    {
        public bool IsRefused => RefusalCode is not null;
    }

    /// <summary>Resolves the universe once and applies the tick-unit gate (spec P3-04b2, D4, D7); a refusal has the code <c>simulate.universe</c> or <c>simulate.tick_unit</c>.</summary>
    public static async Task<Gate> ResolveAsync(string? basket, IReadOnlyList<string> symbols, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var universe = await UniverseResolver.ResolveAsync(basket, symbols, provider, cancellationToken);
        if (universe.IsRefused)
        {
            return new Gate(universe, "simulate.universe", universe.Problems);
        }

        return TickUnitCheck.Suspect(universe.Probes) is { Count: > 0 } suspects
            ? new Gate(universe, "simulate.tick_unit", TickProblems(suspects))
            : new Gate(universe, null, []);
    }

    /// <summary>The fill rules of a mode: the defaults, or the defaults without slippage.</summary>
    public static FillRules Rules(FillsMode mode) =>
        mode == FillsMode.NoSlippage ? FillRules.Create(FillRules.Default.EntryLimitPercent, SlippageConfig.None).Value : FillRules.Default;

    /// <summary>The request of one run. A null timeframe is the default of the built-in strategy.</summary>
    public static SimulationRequest Request(
        string strategyId,
        IReadOnlyDictionary<string, double> parameters,
        string? timeframe,
        DateOnly from,
        DateOnly to,
        SimulateUniverse universe,
        ISimulationEngine engine,
        FillRules rules,
        CostChoice costs,
        bool acknowledgeCsvAdjusted,
        ExitChoice? exits = null,
        int? strategyVersion = null,
        string? strategyHash = null)
    {
        var frame = timeframe
            ?? engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, strategyId, StringComparison.Ordinal))?.DefaultTimeframe.ToCode()
            ?? Timeframe.M15.ToCode();
        var fills = new FillRulesInput(rules.EntryLimitPercent, rules.Slippage.Ticks, rules.Slippage.BarRangeFrac, rules.Slippage.StopExitExtraTicks);
        return new SimulationRequest(strategyId, parameters, frame, from, to, universe.InstrumentIds, universe.BasketId, costs, acknowledgeCsvAdjusted, null, fills, exits, strategyVersion, strategyHash);
    }

    /// <summary>The line that names the stored version a run used (spec P3-02d, question 5): <c>strategy ORB version 1, hash ...</c>; none for a strategy that is not stored.</summary>
    public static async Task<string?> StrategyLineAsync(IServiceProvider provider, string hash, CancellationToken cancellationToken)
    {
        var store = provider.GetService<IStrategyStore>();
        var stored = store is null ? null : await store.GetByHashAsync(hash, cancellationToken);
        return stored is null ? null : "strategy " + stored.StrategyId + " version " + stored.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", hash " + stored.Hash;
    }

    /// <summary>Runs the handler.</summary>
    public static async Task<Result<SimulationRun>> RunAsync(IServiceProvider provider, SimulationRequest request, CancellationToken cancellationToken, ExplainBasisSink? basisSink = null)
    {
        var handler = provider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        return await handler.HandleAsync(new RunSimulationQuery(request, null, basisSink), cancellationToken);
    }

    /// <summary>Runs the comparison handler (P4-05): the same request with two exit candidates or more.</summary>
    public static async Task<Result<ComparisonRun>> RunComparisonAsync(IServiceProvider provider, SimulationRequest request, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<IQueryHandler<RunComparisonQuery, Result<ComparisonRun>>>();
        return await handler.HandleAsync(new RunComparisonQuery(request), cancellationToken);
    }

    /// <summary>The second read for the chosen trades (spec P3-04c, C2); nothing is read when no card is wanted.</summary>
    public static async Task<Result<IReadOnlyList<TradeCardData>>> CardsAsync(IServiceProvider provider, SimulationRun run, IReadOnlyList<string> tradeKeys, ExplainBasis? basis, CancellationToken cancellationToken)
    {
        if (tradeKeys.Count == 0)
        {
            return Result.Success<IReadOnlyList<TradeCardData>>([]);
        }

        var handler = provider.GetRequiredService<IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>>();
        return await handler.HandleAsync(new BuildTradeCardsQuery(run, tradeKeys, basis), cancellationToken);
    }

    /// <summary>Logs the first stale difference, or the first mismatch, of a refused second read, if the error is one.</summary>
    public static void LogStale(ILogger logger, Error error)
    {
        if (error.Fields is [{ } field, ..])
        {
            if (error.Code == SimulationErrors.CardsStaleCode)
            {
                SimulateLog.CardsStale(logger, field.Field, field.Code ?? string.Empty);
            }
            else if (error.Code == SimulationErrors.CardsMismatchCode)
            {
                SimulateLog.CardsMismatch(logger, field.Field, field.Code ?? string.Empty);
            }
        }
    }

    /// <summary>Prints the usage errors and the usage text to stderr; exit 2.</summary>
    public static int Usage(SimulateConsole console, IReadOnlyList<string> errors, string usage)
    {
        foreach (var error in errors)
        {
            console.Error.WriteLine(error);
        }

        console.Error.Write(usage);
        return SimulateCommand.UsageError;
    }

    /// <summary>Logs a refusal and prints its problems (at most <see cref="SimulateCommand.MaxProblems"/>); exit 1.</summary>
    public static async Task<int> RefuseAsync(SimulateConsole console, ILogger logger, string code, IReadOnlyList<string> problems)
    {
        SimulateLog.Refused(logger, code, problems.Count);
        foreach (var line in problems.Take(SimulateCommand.MaxProblems))
        {
            await console.Error.WriteLineAsync(line);
        }

        if (problems.Count > SimulateCommand.MaxProblems)
        {
            await console.Error.WriteLineAsync("and " + (problems.Count - SimulateCommand.MaxProblems).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more problems");
        }

        return SimulateCommand.Refused;
    }

    /// <summary>One <c>path: code: message</c> per problem of a refused run, at most <see cref="SimulateCommand.MaxProblems"/>; a refusal without fields is one <c>code: message</c> line.</summary>
    public static List<string> Problems(Error error) =>
        error.Fields is { Count: > 0 } fields
            ? [.. fields.Select(f => f.Field + ": " + (f.Code ?? error.Code) + ": " + f.Message)]
            : [error.Code + ": " + error.Message];

    /// <summary>The lines of a refused tick gate.</summary>
    public static List<string> TickProblems(IReadOnlyList<(TickProbe Probe, decimal ConvertedRupees)> suspects)
    {
        var lines = new List<string>
        {
            "simulate.tick_unit: " + suspects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " share or ETF rows have a stored tick that reads as less than one paisa after the paise conversion; the master table may hold rupees (ADR 0038 item 6). Nothing was read.",
        };
        lines.AddRange(suspects.Take(3).Select(s =>
            "  " + SimulateOptions.Show(s.Probe.Symbol) + ": stored " + SimulateSummary.Rupees(s.Probe.StoredTick!.Value)
            + ", converted " + SimulateSummary.Rupees(s.ConvertedRupees) + " rupee"));
        return lines;
    }
}
