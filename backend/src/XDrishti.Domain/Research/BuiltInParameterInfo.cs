namespace XDrishti.Domain.Research;

/// <summary>
/// A parameter of a built-in strategy as the catalog lists it (spec P3-04b2, D3): its name, the default a run uses when it is not given, and a one-line note. A parameter may declare the range its value must lie in (spec P3-05, Scope 3;
/// ADR 0041 Amendment 2): <paramref name="Min"/> and <paramref name="Max"/> (null = no bound; the maximum is inclusive) and <paramref name="MinExclusive"/> (the minimum is excluded). The registry checks a finite value against it.
/// </summary>
public sealed record BuiltInParameterInfo(string Name, double Default, string Note, double? Min = null, double? Max = null, bool MinExclusive = false)
{
    /// <summary>True when the parameter declares a bound.</summary>
    public bool HasRange => Min is not null || Max is not null;

    /// <summary>Whether a finite <paramref name="value"/> lies in the declared range (always true when none is declared).</summary>
    public bool Contains(double value) =>
        (Min is not { } min || (MinExclusive ? value > min : value >= min)) && (Max is not { } max || value <= max);

    /// <summary>The declared range as the listing and the refusal print it: <c>0 &lt; x &lt;= 3</c>; one side when one bound is declared; empty when none is.</summary>
    public string RangeText()
    {
        if (!HasRange)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        if (Min is { } min)
        {
            text.Append(min.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(MinExclusive ? " < " : " <= ");
        }

        text.Append('x');
        if (Max is { } max)
        {
            text.Append(" <= ").Append(max.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
