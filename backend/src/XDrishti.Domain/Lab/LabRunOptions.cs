namespace XDrishti.Domain.Lab;

/// <summary>
/// The optional choices of a Lab post that change what the run does (CF-05, ADR 0229): <c>slippage: conservative</c> (tiered slippage floors, tier C and unavailable excluded) and <c>stress</c> (the cost stress block; default on for <c>robust</c>, off for <c>quick</c> and <c>full</c>).
/// CF-07 (ADR 0231) adds <c>nulls=off</c> (the chance and plateau block of a robust run is left out) and <c>draws=N</c> (an own number of random-entry seeds, only when it differs from the configured one); both are what-ifs.
/// <see cref="Unknown"/> is the first stored token that is not one of these (a stored text this code cannot read); the runner fails such a run. <see cref="ToText"/> is null when nothing differs from the defaults, so a post without options keeps its run key and its stored row.
/// </summary>
public sealed record LabRunOptions(bool ConservativeSlippage, bool? Stress, string? Unknown = null, bool NullsOff = false, int? Draws = null)
{
    /// <summary>No choice made.</summary>
    public static LabRunOptions None { get; } = new(false, null);

    /// <summary>The stored text (<c>slippage=conservative;stress=on|off</c>, fixed order); null when nothing was chosen.</summary>
    public string? ToText()
    {
        var parts = new List<string>(2);
        if (ConservativeSlippage)
        {
            parts.Add("slippage=conservative");
        }

        if (Stress is { } stress)
        {
            parts.Add(stress ? "stress=on" : "stress=off");
        }

        AddNulls(parts);
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    private void AddNulls(List<string> parts)
    {
        if (NullsOff)
        {
            parts.Add("nulls=off");
        }

        if (Draws is { } draws)
        {
            parts.Add("draws=" + draws.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Reads the stored text; null or empty is no choice. A token that is not a known option, or a repeated one, is kept in <see cref="Unknown"/> and never guessed at.</summary>
    public static LabRunOptions Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        var conservative = false;
        bool? stress = null;
        string? unknown = null;
        var seenStress = false;
        var nullsOff = false;
        int? draws = null;
        foreach (var part in text.Split(';'))
        {
            switch (part)
            {
                case "slippage=conservative" when !conservative:
                    conservative = true;
                    break;
                case "stress=on" or "stress=off" when !seenStress:
                    seenStress = true;
                    stress = part == "stress=on";
                    break;
                case "nulls=off" when !nullsOff:
                    nullsOff = true;
                    break;
                case var d when draws is null && d.StartsWith("draws=", StringComparison.Ordinal) && int.TryParse(d.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 && d == "draws=" + n.ToString(System.Globalization.CultureInfo.InvariantCulture):
                    draws = n;
                    break;
                default:
                    unknown ??= part;
                    break;
            }
        }

        return new LabRunOptions(conservative, stress, unknown, nullsOff, draws);
    }

    /// <summary>Whether the cost stress block is wanted: the explicit choice, else on for a robust run and off for the others. A post that spells the default is the same run as one that leaves it out.</summary>
    public bool StressOn(bool robust) => Stress ?? robust;

    /// <summary>The text that goes into the run's configuration hash: only what differs from the default of the method, so existing runs keep their key.</summary>
    public string? KeyText(bool robust)
    {
        var parts = new List<string>(2);
        if (ConservativeSlippage)
        {
            parts.Add("slippage=conservative");
        }

        if (Stress is { } stress && stress != robust)
        {
            parts.Add(stress ? "stress=on" : "stress=off");
        }

        AddNulls(parts);
        return parts.Count == 0 ? null : string.Join(';', parts);
    }
}
