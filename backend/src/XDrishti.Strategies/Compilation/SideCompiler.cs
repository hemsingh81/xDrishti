using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Indicators;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The rules of one side (spec P3-02b): compiles every item of every list as its own condition with the context of the evaluation timeframe and the declared parameters, checks the entry, the stop, the exits and the
/// overlays, and merges the features of everything into the one list a frame is created from. Lists are never joined into one text, so no limit of the language is reached by a long list, no span points at text the author
/// did not write and no type error mentions an <c>and</c> the author never wrote.
/// </summary>
internal sealed class SideCompiler(ProblemSink sink, DeclaredParams declared)
{
    /// <summary>What the compile of a side produced; every member is set when the compile has no error.</summary>
    public sealed record Parts(
        CompiledList? Setup,
        CompiledList? Trigger,
        CompiledList? Invalidate,
        EntryKind Entry,
        StopPlan? Stop,
        IReadOnlyList<ExitPlan> Exits,
        IReadOnlyList<OverlayPlan> Overlays,
        IReadOnlyList<FeatureSpec> Features,
        int HistoryDepth,
        bool NeedsContext,
        bool NeedsExpiryDays);

    public Parts Compile(SideRules rules, Side side, Timeframe evaluationTimeframe)
    {
        var prefix = side == Side.Buy ? "buy" : "sell";
        var context = new CompileContext(evaluationTimeframe, declared.Values);
        var setup = CompileList(rules.Setup, prefix + ".setup", context);
        var trigger = rules.Trigger is null ? MissingTrigger(prefix + ".trigger") : CompileList(rules.Trigger, prefix + ".trigger", context);
        var invalidate = CompileList(rules.Invalidate, prefix + ".invalidate", context);
        var entry = CheckEntry(rules.Entry, prefix + ".entry");

        var quantities = new QuantityReader(declared, sink);
        (StopPlan Plan, IReadOnlyList<FeatureSpec> Features)? stop = null;
        if (rules.Stop is null)
        {
            sink.Error(prefix + ".stop", ModelCodes.StopMissing, "The side has no stop: write one, for example fixed_pct with pct 1.");
        }
        else
        {
            stop = new StopCompiler(sink, quantities).Compile(rules.Stop, side, evaluationTimeframe, prefix + ".stop");
        }

        var exitCompiler = new ExitCompiler(sink, quantities);
        var exits = exitCompiler.CompileExits(rules.Exits, evaluationTimeframe, prefix + ".exits");
        var overlays = exitCompiler.CompileOverlays(rules.Overlays, prefix + ".overlays");

        var conditions = new List<CompiledCondition>();
        foreach (var list in new[] { setup, trigger, invalidate })
        {
            if (list is not null)
            {
                conditions.AddRange(list.Items.Select(i => i.Condition));
            }
        }

        var plan = ConditionSet.Merge(conditions);
        var features = new List<FeatureSpec>(plan.Features);
        var seen = new HashSet<FeatureSpec>(features);
        foreach (var spec in (stop?.Features ?? []).Concat(exits.SelectMany(e => e.Reads)))
        {
            if (seen.Add(spec))
            {
                features.Add(spec);
            }
        }

        // A frame cannot be created without a feature and answers BarClosed only for a timeframe it has one on: a side that reads nothing on the evaluation timeframe (only tf("1h") features, or only `time`) gets `close` there.
        if (!features.Exists(f => f.Timeframe == evaluationTimeframe) && IndicatorCatalog.TryGet("close", out var close))
        {
            features.Add(close.DefaultSpec(evaluationTimeframe));
        }

        return new Parts(setup, trigger, invalidate, entry, stop?.Plan, exits, overlays, features.AsReadOnly(), plan.HistoryDepth, plan.NeedsContext, plan.NeedsExpiryDays);
    }

    private CompiledList? MissingTrigger(string path)
    {
        sink.Error(path, ModelCodes.TriggerMissing, "The side has no trigger: write at least one condition, for example crosses_above(close, vwap).");
        return null;
    }

    private EntryKind CheckEntry(EntrySpec? entry, string path)
    {
        if (entry is null)
        {
            sink.Error(path, ModelCodes.EntryMissing, "The side has no entry: write one, for example at_close.");
            return default;
        }

        if (!Enum.IsDefined(entry.Kind))
        {
            sink.Error(path + ".kind", ModelCodes.UndefinedValue, "The entry kind " + (int)entry.Kind + " is not one of the entry kinds.");
        }

        return entry.Kind;
    }

    /// <summary>One list, each item compiled alone. Null for an absent list and for a list with a problem of its own (the items that did compile are still checked and reported).</summary>
    private CompiledList? CompileList(ConditionList? list, string path, CompileContext context)
    {
        if (list is null)
        {
            return null;
        }

        var texts = list.Items ?? [];
        var valid = true;
        if (!Enum.IsDefined(list.Mode))
        {
            sink.Error(path + ".mode", ModelCodes.UndefinedValue, "The list mode " + (int)list.Mode + " is not all or any.");
            valid = false;
        }

        if (texts.Count == 0)
        {
            sink.Error(path, ModelCodes.ListEmpty, "The list has no condition: write at least one, or leave the list out.");
            return null;
        }

        if (texts.Count > StrategyDefinition.MaxItemsPerList)
        {
            sink.Error(path, ModelCodes.ListTooLong, "The list has " + texts.Count + " conditions, but at most " + StrategyDefinition.MaxItemsPerList + " are allowed; only the first " + StrategyDefinition.MaxItemsPerList + " are checked.");
            valid = false;
        }

        var items = new List<CompiledItem>(Math.Min(texts.Count, StrategyDefinition.MaxItemsPerList));
        for (var i = 0; i < Math.Min(texts.Count, StrategyDefinition.MaxItemsPerList); i++)
        {
            var itemPath = ModelChecks.Index(path, i);
            var result = ConditionCompiler.Compile(texts[i] ?? string.Empty, context);
            foreach (var problem in result.Problems)
            {
                sink.Condition(itemPath, problem);
            }

            if (result.Condition is { } condition)
            {
                items.Add(new CompiledItem(itemPath, condition));
            }
            else
            {
                valid = false;
            }
        }

        return valid ? new CompiledList(path, list.Mode, items.AsReadOnly()) : null;
    }
}
