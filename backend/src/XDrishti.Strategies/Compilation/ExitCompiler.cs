using System.Collections.ObjectModel;
using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The candidate exits and the overlays of a side (spec P3-02b): a candidate policy once, its parameters checked against the table of <see cref="ExitTables"/> (required, not used, scalar or list, bounds) or, for the policies
/// without a table yet and for the overlays, only their shape (a name, one to ten values, each a finite number or a declared parameter). The features a policy reads are built from the catalog's default spec and judged by the
/// catalog. Features are read on the evaluation timeframe.
/// </summary>
internal sealed class ExitCompiler(ProblemSink sink, QuantityReader quantities)
{
    /// <summary>The plans of the candidates, in the order of the model; the policies with a problem are left out (the compile fails).</summary>
    public IReadOnlyList<ExitPlan> CompileExits(IReadOnlyList<ExitSpec>? exits, Timeframe evaluationTimeframe, string path)
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
            if (duplicate)
            {
                sink.Error(at, ModelCodes.DuplicateExit, "The exit " + ExitTables.PolicyName(exit.Policy) + " is listed twice; the Lab compares each policy once.");
            }

            var before = sink.ErrorCount;
            var table = ExitTables.Checked.GetValueOrDefault(exit.Policy);
            var (values, reads) = table is null ? ShapeOnly(exit.Parameters, at) : Checked(exit.Policy, table, exit.Parameters, evaluationTimeframe, at);
            if (!duplicate && sink.ErrorCount == before)
            {
                plans.Add(new ExitPlan(exit.Policy, values, reads));
            }
        }

        return plans.AsReadOnly();
    }

    /// <summary>The plans of the overlays, one of each kind at most.</summary>
    public IReadOnlyList<OverlayPlan> CompileOverlays(IReadOnlyList<OverlaySpec>? overlays, string path)
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
            if (duplicate)
            {
                sink.Error(at, ModelCodes.DuplicateExit, "The overlay " + ExitTables.OverlayName(overlay.Kind) + " is listed twice.");
            }

            var before = sink.ErrorCount;
            var (values, _) = ShapeOnly(overlay.Parameters, at);
            if (!duplicate && sink.ErrorCount == before)
            {
                plans.Add(new OverlayPlan(overlay.Kind, values));
            }
        }

        return plans.AsReadOnly();
    }

    private static bool IsList(ParameterSpec? spec, int count) => spec?.Shape == ParameterShape.List || (spec is null && count > 1);

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private (IReadOnlyDictionary<string, IReadOnlyList<double>> Values, IReadOnlyList<FeatureSpec> Reads) Checked(
        ExitPolicy policy,
        IReadOnlyList<ParameterSpec> table,
        IReadOnlyList<ExitParameter>? parameters,
        Timeframe evaluationTimeframe,
        string path)
    {
        var values = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);
        var atrPeriod = (double?)null;
        foreach (var parameter in parameters ?? [])
        {
            if (!TryName(parameter, path, given, out var name))
            {
                continue;
            }

            var spec = table.FirstOrDefault(p => p.Name == name);
            if (spec is null)
            {
                sink.Error(path + "." + name, ModelCodes.FieldNotUsed, ExitTables.PolicyName(policy) + " has no parameter `" + name + "`; its parameters are " + string.Join(", ", table.Select(p => "`" + p.Name + "`")) + ".");
                continue;
            }

            if (ReadValues(parameter, spec, path) is { } read)
            {
                values[name] = read;
                if (spec.IsAtrPeriod)
                {
                    atrPeriod = read[0];
                }
            }
        }

        foreach (var spec in table.Where(p => p.Required && !given.Contains(p.Name)))
        {
            sink.Error(path + "." + spec.Name, ModelCodes.FieldMissing, ExitTables.PolicyName(policy) + " needs `" + spec.Name + "`.");
        }

        var reads = new List<FeatureSpec>(1);
        if (table.Any(p => p.IsAtrPeriod) && IndicatorCatalog.TryGet("atr", out var atr))
        {
            var spec = atr.DefaultSpec(evaluationTimeframe);
            if (atrPeriod is { } period)
            {
                spec = spec with { P1 = period };
            }
            else
            {
                values["atr_period"] = new ReadOnlyCollection<double>([spec.P1]);
            }

            foreach (var error in CatalogCheck.Answer(spec))
            {
                var field = CatalogCheck.FieldOf(error);
                sink.Error(field == "p1" ? path + ".atr_period" : path, ModelCodes.FeatureInvalid, error.Message);
            }

            reads.Add(spec);
        }

        return (values.AsReadOnly(), reads.AsReadOnly());
    }

    private (IReadOnlyDictionary<string, IReadOnlyList<double>> Values, IReadOnlyList<FeatureSpec> Reads) ShapeOnly(IReadOnlyList<ExitParameter>? parameters, string path)
    {
        var values = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters ?? [])
        {
            if (TryName(parameter, path, given, out var name) && ReadValues(parameter, null, path) is { } read)
            {
                values[name] = read;
            }
        }

        return (values.AsReadOnly(), []);
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

    /// <summary>The numbers of a parameter, or null (the problem reported). Checks the count against the shape, resolves parameter names and, when the table gives bounds, the range of each value.</summary>
    private ReadOnlyCollection<double>? ReadValues(ExitParameter parameter, ParameterSpec? spec, string path)
    {
        var fieldPath = path + "." + parameter.Name;
        var quantitiesGiven = parameter.Values;
        var count = quantitiesGiven?.Count ?? 0;
        if (count < 1 || count > ExitTables.MaxListValues || (spec?.Shape == ParameterShape.Scalar && count != 1))
        {
            sink.Error(
                fieldPath,
                ModelCodes.FieldShape,
                "`" + parameter.Name + "` takes " + (spec?.Shape == ParameterShape.Scalar ? "exactly one value" : "one to " + ExitTables.MaxListValues + " values") + ", but " + count + (count == 1 ? " is" : " are") + " given.");
            return null;
        }

        var numbers = new double[count];
        var ok = true;
        var list = IsList(spec, count);
        for (var j = 0; j < count; j++)
        {
            var valuePath = list ? ModelChecks.Index(fieldPath, j) : fieldPath;
            if (!quantities.TryRead(quantitiesGiven![j], valuePath, out numbers[j]))
            {
                ok = false;
            }
            else if (spec is { IsAtrPeriod: false } && !double.IsNaN(spec.Max) && !InRange(numbers[j], spec))
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
