using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// The engine port without an engine: it records what the use case asks of it and answers with a scripted replay (by default the replay of an instrument that is skipped as the preset says, or as <c>no_bars</c> for an empty list, and has no
/// trades). Application cannot name the simulation library, so the handler is tested against this.
/// </summary>
internal sealed class FakeSimulationEngine : ISimulationEngine
{
    public string EngineVersion => "fake-1";

    /// <summary>What <see cref="Prepare"/> was asked, in order.</summary>
    public List<(string StrategyId, IReadOnlyDictionary<string, double> Parameters, Timeframe Timeframe, FillRules? Fills)> Prepared { get; } = [];

    /// <summary>The inputs of <see cref="Replay"/>, in order.</summary>
    public List<InstrumentReplayInput> Replays { get; } = [];

    /// <summary>The answer of <see cref="Prepare"/>; null means success with <see cref="Warnings"/>.</summary>
    public Result<IPreparedStrategy>? PrepareResult { get; set; }

    public List<FieldError> Warnings { get; } = [];

    /// <summary>The replay to return; null means the default.</summary>
    public Func<InstrumentReplayInput, InstrumentReplay>? Script { get; set; }

    /// <summary>Runs at the start of every replay (a test moves the clock or throws).</summary>
    public Action<InstrumentReplayInput>? OnReplay { get; set; }

    public Result<IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills)
    {
        Prepared.Add((strategyId, parameters, evaluationTimeframe, fills));
        return PrepareResult ?? Result.Success<IPreparedStrategy>(new FakePrepared(
            new StrategyRecord(strategyId, parameters, "{}", new string('a', 64), evaluationTimeframe, [Side.Buy, Side.Sell]),
            Warnings));
    }

    public InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input)
    {
        Replays.Add(input);
        OnReplay?.Invoke(input);
        return Script?.Invoke(input) ?? Default(input);
    }

    /// <summary>The replay of an instrument that was read and has no trades: skipped as the preset says (<c>no_bars</c> for an empty list), with the facts a replay keeps of the bars it was given.</summary>
    public static InstrumentReplay Default(InstrumentReplayInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new InstrumentReplay(Record(input, input.RawBars.Count == 0 ? SkipReason.NoBars : input.PresetSkip, input.SkipDetail), [], []);
    }

    /// <summary>The record of an instrument that was given to the engine, with the bars hash and counts of the list.</summary>
    public static InstrumentRun Record(InstrumentReplayInput input, SkipReason? skipped, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return InstrumentRun.NotFound(input.InstrumentId) with
        {
            Symbol = input.Symbol,
            Skipped = skipped,
            SkipDetail = detail,
            BarsRead = input.RawBars.Count,
            BarsSha256 = BarStreamHash.Compute(input.RawBars),
            TickSize = input.Tick?.Size,
            TickFromFallback = input.Tick?.FromFallback ?? false,
        };
    }

    /// <summary>A trade of the instrument on the date, enough to travel through the assembly.</summary>
    public static SimTrade Trade(InstrumentReplayInput input, DateOnly date, Side side = Side.Buy)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entry = new DateTimeOffset(date.Year, date.Month, date.Day, 9, 45, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var bar = new Bar(entry - 900, 99, 100.5, 98, 100, 1_000);
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", input.InstrumentId, input.Symbol, side, date, entry, Timeframe.M15, bar, 15, true,
            entry, 100.5, 98, 105.5, entry + 3600, 105.5, ExitReason.Target, 2.0, 0.1, 2.0, FillBasis.Exact, CostBasis.None, null,
            2.0, 0, 100.5, 105.5, EntryFill.AtLimit, false, 1, 0);
    }

    private sealed class FakePrepared(StrategyRecord record, IReadOnlyList<FieldError> warnings) : IPreparedStrategy
    {
        public StrategyRecord Record => record;

        public IReadOnlyList<FieldError> Warnings => warnings;
    }
}
