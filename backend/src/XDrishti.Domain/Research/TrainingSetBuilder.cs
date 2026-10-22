using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>Builds the <see cref="TrainingSet"/> of the labels of many trades (P6-03, ADR 0065): labels each on the basis of its own run, counts what was left out and refuses a key that appears twice. Pure and deterministic.</summary>
public static class TrainingSetBuilder
{
    /// <summary>Builds the set.</summary>
    /// <param name="runKeys">The keys of the runs the trades came from.</param>
    /// <param name="trades">The trades of those runs.</param>
    public static Result<TrainingSet> Build(IEnumerable<string> runKeys, IEnumerable<SimTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(runKeys);
        ArgumentNullException.ThrowIfNull(trades);
        var keys = runKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var labels = new List<TripleBarrierLabel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int unresolved = 0, noTarget = 0;
        DateOnly? from = null, to = null;
        foreach (var trade in trades)
        {
            if (!seen.Add(trade.Key))
            {
                return Result.Failure<TrainingSet>(Error.Conflict("training_set.duplicate_key", "Two trades have the key " + trade.Key + "."));
            }

            var label = TripleBarrier.Label(trade);
            if (label.IsFailure)
            {
                if (label.Error.Code == "label.no_target")
                {
                    noTarget++;
                }
                else
                {
                    unresolved++;
                }

                continue;
            }

            labels.Add(label.Value);
            from = from is null || trade.TradeDate < from ? trade.TradeDate : from;
            to = to is null || trade.TradeDate > to ? trade.TradeDate : to;
        }

        if (from is null || to is null)
        {
            return Result.Failure<TrainingSet>(Error.Validation("training_set.empty", "No trade could be labelled."));
        }

        labels.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var positives = labels.Count(l => l.Label == 1);
        return Result.Success(new TrainingSet(IdOf(keys, labels), TripleBarrier.RuleVersion, keys, from.Value, to.Value, positives, labels.Count - positives, unresolved, noTarget, labels));
    }

    private static string IdOf(string[] runKeys, List<TripleBarrierLabel> labels)
    {
        var text = new StringBuilder();
        text.Append(TripleBarrier.RuleVersion).Append('\n').AppendJoin(',', runKeys).Append('\n');
        foreach (var l in labels)
        {
            text.Append(CultureInfo.InvariantCulture, $"{l.Key}|{l.Label}|{l.Outcome}|{l.Basis}|{l.R:R}|{l.MinutesToResolution}|{l.ResolvedEpochSeconds}\n");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
