using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>
/// How the trades of a simulation training job become cells (AU-13), shared by the training and by the model's hold-out look (CF-10e part 3): one cash cell per strategy, timeframe and side, with the fixed join keys <see cref="Style"/> and <see cref="ExitLabel"/>, costed or gross as the trades are. One definition, so the
/// cells a model is scored with at the hold-out are built the way its training rows were.
/// </summary>
internal static class SimTrainingCells
{
    /// <summary>Join key of the point-in-time cells only; it carries no meaning here.</summary>
    public const string Style = "swing";

    /// <summary>Join key of the point-in-time cells only.</summary>
    public const string ExitLabel = "simulated";

    /// <summary>The cell a trade belongs to.</summary>
    public static CellId CellOf(SimTrade t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), Style, ExitLabel, Expression: Expression.Cash);
    }

    /// <summary>The cell inputs of the trades in cell-key order, or a failure when one cell mixes costed and gross trades.</summary>
    public static Result<List<CellInput>> BuildInputs(IReadOnlyList<SimTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var inputs = new Dictionary<string, (CellId Cell, bool Costed, List<CellTrade> Trades)>(StringComparer.Ordinal);
        foreach (var t in trades)
        {
            var id = CellOf(t);
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            if (!inputs.TryGetValue(id.Key, out var cell))
            {
                cell = (id, costed, []);
                inputs[id.Key] = cell;
            }

            if (cell.Costed != costed)
            {
                return Result.Failure<List<CellInput>>(LearningErrors.MixedBasis(id.Key));
            }

            cell.Trades.Add(new CellTrade(SessionClock.DateOf(t.ExitEpochSeconds), costed ? t.Costing!.RNet : t.RFilled, t.MaeR, t.MfeR, t.TradeDate));
        }

        return Result.Success(inputs.Values.OrderBy(c => c.Cell.Key, StringComparer.Ordinal).Select(c => new CellInput(c.Cell, c.Costed, c.Trades)).ToList());
    }

    /// <summary>A stable bucket of a trade key, so thinning keeps the same trades on every run.</summary>
    public static uint Bucket(string key) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0);

    /// <summary>The timeframe the training job ran a strategy version on: the built-in's default, else 15 minutes when the strategy allows it (or names none), else its first. One definition for the job and for the model's hold-out look.</summary>
    public static Timeframe DefaultTimeframe(ISimulationEngine engine, string strategyId, StrategyDescription description)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(description);
        return engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, strategyId, StringComparison.Ordinal))?.DefaultTimeframe
            ?? (description.Timeframes.Contains(Timeframe.M15) || description.Timeframes.Count == 0 ? Timeframe.M15 : description.Timeframes[0]);
    }
}
