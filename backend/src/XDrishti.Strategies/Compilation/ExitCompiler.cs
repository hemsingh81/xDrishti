using System.Collections.ObjectModel;
using System.Globalization;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The candidate exits and the overlays of a side (specs P3-02b and P3-02b2): a candidate policy once, its parameters checked against the table of <see cref="ExitTables"/> (required, not used, scalar or list, whole,
/// bounds), then the relations of <see cref="ExitRelations"/>. The features a policy reads are built from the catalog's default spec and judged by the catalog. Features are read on the evaluation timeframe. A policy or an
/// overlay no simulator runs yet gets one <c>exit_not_evaluated</c> warning.
/// </summary>
internal sealed class ExitCompiler(ProblemSink sink, QuantityReader quantities)
{
    /// <summary>The plans of the candidates, in the order of the model; the policies with a problem are left out (the compile fails).</summary>
    public IReadOnlyList<ExitPlan> CompileExits(IReadOnlyList<ExitSpec>? exits, ExitContext context, string path)
    {
        var plans = new List<ExitPlan>();
        if (exits is null || exits.Count == 0)
        {
            sink.Error(path, ModelCodes.ExitsEmpty, "The side has no exit: list at least one policy, for example FIXED_RR.");
            return plans.AsReadOnly();
        }

        var seen = new HashSet<ExitPolicy>();
        for (var i = 0; i < exits.Count; i++)
        {
            var at = ModelChecks.Index(path, i);
            var exit = exits[i];
            if (exit is null)
            {
                sink.Error(at, ModelCodes.FieldMissing, "The exit is missing.");
                continue;
            }

            if (!Enum.IsDefined(exit.Policy))
            {
                sink.Error(at + ".policy", ModelCodes.UndefinedValue, "The exit policy " + (int)exit.Policy + " is not one of the exit policies.");
                continue;
            }

            var duplicate = !seen.Add(exit.Policy);
            var name = ExitTables.PolicyName(exit.Policy);
            if (duplicate)
            {
                sink.Error(at, ModelCodes.DuplicateExit, "The exit " + name + " is listed twice; the Lab compares each policy once.");
            }

            var before = sink.ErrorCount;
            var (values, reads) = Checked(name, ExitTables.Checked[exit.Policy], exit.Parameters, ExitTables.Read(exit.Policy, context.Side), null, context, at);
            ExitRelations.Check(sink, exit.Policy, values, at);
            if (!duplicate && !ExitTables.RunBySimulator.Contains(exit.Policy))
            {
                sink.Warning(at, ModelCodes.ExitNotEvaluated, "The exit " + name + " is checked, but nothing runs it yet (P4); a simulator refuses a strategy that uses it.");
            }

            if (!duplicate && sink.ErrorCount == before)
            {
                plans.Add(new ExitPlan(exit.Policy, values, reads));
            }
        }

        return plans.AsReadOnly();
    }

    /// <summary>The plans of the overlays, one of each kind at most.</summary>
    public IReadOnlyList<OverlayPlan> CompileOverlays(IReadOnlyList<OverlaySpec>? overlays, ExitContext context, string path)
    {
        var plans = new List<OverlayPlan>();
        var seen = new HashSet<OverlayKind>();
        for (var i = 0; i < (overlays?.Count ?? 0); i++)
        {
            var at = ModelChecks.Index(path, i);
            var overlay = overlays![i];
            if (overlay is null)
            {
                sink.Error(at, ModelCodes.FieldMissing, "The overlay is missing.");
                continue;
            }

            if (!Enum.IsDefined(overlay.Kind))
            {
                sink.Error(at + ".kind", ModelCodes.UndefinedValue, "The overlay kind " + (int)overlay.Kind + " is not one of the overlays.");
                continue;
            }

            var duplicate = !seen.Add(overlay.Kind);
            var name = ExitTables.OverlayName(overlay.Kind);
            if (duplicate)
            {
                sink.Error(at, ModelCodes.DuplicateExit, "The overlay " + name + " is listed twice.");
            }

            var before = sink.ErrorCount;
            var (values, _) = Checked(name, ExitTables.OverlayChecked[overlay.Kind], overlay.Parameters, null, StyleRule(overlay.Kind, context), context, at);
            ExitRelations.Check(sink, overlay.Kind, values, context, at);
            if (!duplicate)
            {
                sink.Warning(at, ModelCodes.ExitNotEvaluated, "The overlay " + name + " is checked, but nothing runs it yet (P4)" + (overlay.Kind == OverlayKind.EventExit ? " and the results calendar it needs is missing (P15)" : string.Empty) + "; a simulator refuses a strategy that uses it.");
            }

            if (!duplicate && sink.ErrorCount == before)
            {
                plans.Add(new OverlayPlan(overlay.Kind, values));
            }
        }

        return plans.AsReadOnly();
    }

    /// <summary>The time stop is given in bars for an intraday strategy and in sessions for a swing one: the field of its own style is required, the field of the other is refused. Null for any other overlay and for an undefined style.</summary>
    private static (string Required, string Refused)? StyleRule(OverlayKind kind, ExitContext context) =>
        kind != OverlayKind.TimeStop || !Enum.IsDefined(context.Style)
            ? null
            : context.Style == Style.Intraday ? ("intraday_bars", "swing_sessions") : ("swing_sessions", "intraday_bars");

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private (IReadOnlyDictionary<string, IReadOnlyList<double>> Values, IReadOnlyList<FeatureSpec> Reads) Checked(
        string label,
        IReadOnlyList<ParameterSpec> table,
        IReadOnlyList<ExitParameter>? parameters,
        (string Indicator, string Parameter)? read,
        (string Required, string Refused)? style,
        ExitContext context,
        string path)
    {
        var values = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters ?? [])
        {
            if (!TryName(parameter, path, given, out var name))
            {
                continue;
            }

            var spec = table.FirstOrDefault(p => p.Name == name);
            if (spec is null)
            {
                sink.Error(path + "." + name, ModelCodes.FieldNotUsed, ExitTables.RefusedNote(label, name) ?? label + " has no parameter `" + name + "`; its parameters are " + string.Join(", ", table.Select(p => "`" + p.Name + "`")) + ".");
                continue;
            }

            if (style is { } rule && name == rule.Refused)
            {
                var intraday = context.Style == Style.Intraday;
                sink.Error(path + "." + name, ModelCodes.FieldNotUsed, "A " + (intraday ? "intraday" : "swing") + " strategy gives the time stop in " + (intraday ? "bars (`intraday_bars`)" : "sessions (`swing_sessions`)") + ", so `" + name + "` is not used. Leave it out.");
                continue;
            }

            if (ReadValues(parameter, spec, path) is { } numbers)
            {
                values[name] = numbers;
            }
        }

        foreach (var spec in table.Where(p => (p.Required || p.Name == style?.Required) && !given.Contains(p.Name)))
        {
            sink.Error(path + "." + spec.Name, ModelCodes.FieldMissing, label + " needs `" + spec.Name + "`.");
        }

        var reads = new List<FeatureSpec>(1);
        if (read is { } feature && IndicatorCatalog.TryGet(feature.Indicator, out var info))
        {
            var spec = info.DefaultSpec(context.EvaluationTimeframe);
            if (values.TryGetValue(feature.Parameter, out var first))
            {
                spec = spec with { P1 = first[0] };
            }
            else if (given.Contains(feature.Parameter) || table.First(p => p.Name == feature.Parameter).Required)
            {
                // The parameter is missing or has a problem of its own (reported): no feature to judge.
                return (values.AsReadOnly(), reads.AsReadOnly());
            }
            else
            {
                values[feature.Parameter] = new ReadOnlyCollection<double>([spec.P1]);
            }

            foreach (var error in CatalogCheck.Answer(spec))
            {
                var field = CatalogCheck.FieldOf(error);
                sink.Error(field == "p1" ? path + "." + feature.Parameter : path, ModelCodes.FeatureInvalid, error.Message);
            }

            reads.Add(spec);
        }

        return (values.AsReadOnly(), reads.AsReadOnly());
    }

    /// <summary>The name of a parameter when it is a good name given once; otherwise the problem is reported and false returned.</summary>
    private bool TryName(ExitParameter? parameter, string path, HashSet<string> given, out string name)
    {
        name = parameter?.Name ?? string.Empty;
        if (parameter is null || !ModelChecks.IsName(parameter.Name))
        {
            sink.Error(path, ModelCodes.FieldShape, "A parameter name is 1 to 32 characters, a lower-case letter and then lower-case letters, digits or underscores" + (parameter?.Name is { } bad ? ", not " + BindingScope.Quote(bad) + "." : "."));
            return false;
        }

        if (!given.Add(name))
        {
            sink.Error(path + "." + name, ModelCodes.FieldShape, "`" + name + "` is given twice.");
            return false;
        }

        return true;
    }

    /// <summary>The numbers of a parameter, or null (the problem reported). Checks the count against the shape, resolves parameter names, the whole-number rule and the range of each value.</summary>
    private ReadOnlyCollection<double>? ReadValues(ExitParameter parameter, ParameterSpec spec, string path)
    {
        var fieldPath = path + "." + parameter.Name;
        var quantitiesGiven = parameter.Values;
        var count = quantitiesGiven?.Count ?? 0;
        if (count < 1 || count > ExitTables.MaxListValues || (spec.Shape == ParameterShape.Scalar && count != 1))
        {
            sink.Error(
                fieldPath,
                ModelCodes.FieldShape,
                "`" + parameter.Name + "` takes " + (spec.Shape == ParameterShape.Scalar ? "exactly one value" : "one to " + ExitTables.MaxListValues + " values") + ", but " + count + (count == 1 ? " is" : " are") + " given.");
            return null;
        }

        var numbers = new double[count];
        var ok = true;
        var list = spec.Shape == ParameterShape.List;
        for (var j = 0; j < count; j++)
        {
            var valuePath = list ? ModelChecks.Index(fieldPath, j) : fieldPath;
            if (!quantities.TryRead(quantitiesGiven![j], valuePath, out numbers[j]))
            {
                ok = false;
            }
            else if (!spec.IsCatalog && spec.Whole && numbers[j] != Math.Floor(numbers[j]))
            {
                sink.Error(valuePath, ModelCodes.FieldShape, "`" + parameter.Name + "` is " + Format(numbers[j]) + ", but it must be a whole number.");
                ok = false;
            }
            else if (!spec.IsCatalog && !InRange(numbers[j], spec))
            {
                sink.Error(valuePath, ModelCodes.ValueOutOfRange, "`" + parameter.Name + "` is " + Format(numbers[j]) + ", but it must be in " + (spec.MinInclusive ? "[" : "(") + Format(spec.Min) + ", " + Format(spec.Max) + (spec.MaxInclusive ? "]" : ")") + ".");
                ok = false;
            }
        }

        return ok ? Array.AsReadOnly(numbers) : null;
    }

    private static bool InRange(double value, ParameterSpec spec) =>
        (spec.MinInclusive ? value >= spec.Min : value > spec.Min) && (spec.MaxInclusive ? value <= spec.Max : value < spec.Max);
}
