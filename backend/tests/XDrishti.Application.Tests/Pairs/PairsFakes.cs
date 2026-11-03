using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Application.Tests.Pairs;

/// <summary>The call order of the fakes of one test: ledger appends, formation writes, walks and trade writes, in the order they happened.</summary>
internal sealed class Journal
{
    public List<string> Events { get; } = [];
}

internal sealed class FakeLedger(Journal journal) : ITestLedger
{
    private readonly HashSet<Guid> _ids = [];
    private readonly List<LedgerRow> _rows = [];

    public int Appended => _rows.Count;

    public IReadOnlyList<LedgerRow> Rows => _rows;

    public ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken)
    {
        journal.Events.Add("ledger");
        if (!_ids.Add(row.RunId))
        {
            return ValueTask.FromResult(false);
        }

        _rows.Add(row);
        return ValueTask.FromResult(true);
    }

    public ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_rows.Where(r => r.StrategyId == strategyId).Select(r => r.StrategyHash).Distinct().Count());

    public ValueTask<bool> HasHashAsync(string strategyId, string strategyHash, CancellationToken cancellationToken) => ValueTask.FromResult(_rows.Any(r => r.StrategyId == strategyId && r.StrategyHash == strategyHash));

    public ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<LedgerRow>>([.. _rows.Where(r => r.StrategyId == strategyId)]);
}

internal sealed class FakeStore(Journal journal) : IPairResearchStore
{
    private readonly HashSet<string> _formations = [];
    private readonly HashSet<string> _trades = [];

    public List<PairFormationRow> Formations { get; } = [];

    public List<PairTradeRow> Trades { get; } = [];

    public List<PairStoredTrade> Stored { get; set; } = [];

    public List<StoredFormation> Stats { get; set; } = [];

    public int ReadTradesCalls { get; private set; }

    public ValueTask<bool> AppendFormationAsync(PairFormationRow row, CancellationToken cancellationToken)
    {
        journal.Events.Add("formation");
        if (!_formations.Add(row.FormationId))
        {
            return ValueTask.FromResult(false);
        }

        Formations.Add(row);
        return ValueTask.FromResult(true);
    }

    public ValueTask<int> AppendTradesAsync(IReadOnlyList<PairTradeRow> rows, CancellationToken cancellationToken)
    {
        journal.Events.Add("trades");
        var written = 0;
        foreach (var row in rows)
        {
            if (_trades.Add(row.RunId + "|" + row.PairKey + "|" + row.Ordinal))
            {
                Trades.Add(row);
                written++;
            }
        }

        return ValueTask.FromResult(written);
    }

    public ValueTask<IReadOnlyList<PairStoredTrade>> ReadTradesAsync(string cfgLabel, CancellationToken cancellationToken)
    {
        ReadTradesCalls++;
        return ValueTask.FromResult<IReadOnlyList<PairStoredTrade>>([.. Stored.Where(t => t.Labels.Contains(cfgLabel))]);
    }

    public ValueTask<int> CountOtherTradesAsync(string cfgLabel, DateOnly before, CancellationToken cancellationToken) => ValueTask.FromResult(Stored.Count(t => !t.Labels.Contains(cfgLabel) && t.Date < before));

    public ValueTask<IReadOnlyList<StoredFormation>> ReadFormationsAsync(string family, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StoredFormation>>(Stats);
}

internal sealed class FakeSimulator(Journal journal) : IPairSimulator
{
    public List<PairWalkInput> Inputs { get; } = [];

    public Func<PairWalkInput, PairWalkResult> Walker { get; set; } = _ => new PairWalkResult([], PairWalkCounters.Zero);

    public string WalkerVersion => "pair-1";

    public string FillEngineVersion => "sim-9";

    public Result<PairWalkResult> Walk(PairWalkInput input)
    {
        journal.Events.Add("walk");
        Inputs.Add(input);
        return Result.Success(Walker(input));
    }
}

internal sealed class FakeSettings : IPairSettings
{
    public decimal PairNotional { get; set; } = 100_000m;

    public int MaxCandidates { get; set; } = 2000;

    public string? CostSheetId { get; set; }
}

internal sealed class FakeEvaluations : IExtensionEvaluationStore
{
    public List<ExtensionEvaluationRecord> Records { get; } = [];

    public bool HoldOutSpent { get; set; }

    public int HoldOutChecks { get; private set; }

    public ValueTask<Result<bool>> AppendAsync(ExtensionEvaluationRecord record, DateTimeOffset createdAt, CancellationToken cancellationToken)
    {
        if (Records.Any(r => r.EvaluationId == record.EvaluationId))
        {
            return ValueTask.FromResult(Result.Success(false));
        }

        if (record.HoldOutRead && (HoldOutSpent || Records.Any(r => r.HoldOutRead)))
        {
            return ValueTask.FromResult(Result.Failure<bool>(Error.Conflict("extensions.holdout_used", "spent")));
        }

        Records.Add(record);
        return ValueTask.FromResult(Result.Success(true));
    }

    public ValueTask<ExtensionEvaluationRecord?> GetAsync(string evaluationId, CancellationToken cancellationToken) => ValueTask.FromResult(Records.FirstOrDefault(r => r.EvaluationId == evaluationId));

    public ValueTask<bool> HoldOutUsedAsync(string family, string versionKey, CancellationToken cancellationToken)
    {
        HoldOutChecks++;
        return ValueTask.FromResult(HoldOutSpent || Records.Any(r => r.HoldOutRead));
    }

    public ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ExtensionEvaluationRecord>>([.. Records.Where(r => r.Family == family)]);
}

internal static class PairsData
{
    public static double Uniform(SeededRandom rng) => (((rng.NextBits() >> 11) + 0.5) * (1.0 / 9007199254740992.0));

    public static double Normal(SeededRandom rng) => Math.Sqrt(-2.0 * Math.Log(Uniform(rng))) * Math.Cos(2.0 * Math.PI * Uniform(rng));

    public static double[] Walk(SeededRandom rng, int n, double step = 0.01)
    {
        var w = new double[n];
        for (var i = 1; i < n; i++)
        {
            w[i] = w[i - 1] + (step * Normal(rng));
        }

        return w;
    }

    /// <summary>The weekdays ending on <paramref name="last"/>, <paramref name="count"/> of them, ascending.</summary>
    public static List<DateOnly> Sessions(DateOnly last, int count)
    {
        var days = new List<DateOnly>();
        for (var d = last; days.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        days.Reverse();
        return days;
    }

    public static BarRow Day(DateOnly date, double close) =>
        new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)), close, close, close, close, 1000, 375, true, null);
}
