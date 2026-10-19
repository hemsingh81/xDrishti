namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// What the comparison saw of the five ratio fields (D5): how many pairs it compared, how many were not bit-equal, and the largest absolute difference with where it was seen. Exact equality is tried first; only a pair that is not
/// exact is held to the tolerance, so the maximum of a run in which every pair is exact is 0 and no tolerance is needed.
/// </summary>
internal sealed class RatioStats
{
    /// <summary>The tolerance of D5, for the five ratio fields only.</summary>
    internal const double Tolerance = 1e-12;

    private readonly object _gate = new();

    internal long Compared { get; private set; }

    internal long NotExact { get; private set; }

    internal double MaxDifference { get; private set; }

    internal string Worst { get; private set; } = string.Empty;

    /// <summary>Records one pair; true when the pair is within the rule (equal bit for bit, or, failing that, within the tolerance).</summary>
    internal bool Accept(string field, double oracle, double engine, string where)
    {
        var exact = BitConverter.DoubleToInt64Bits(oracle) == BitConverter.DoubleToInt64Bits(engine);
        lock (_gate)
        {
            Compared++;
            if (exact)
            {
                return true;
            }

            NotExact++;
            var difference = Math.Abs(oracle - engine);
            if (!double.IsNaN(difference) && difference > MaxDifference)
            {
                MaxDifference = difference;
                Worst = $"{field} at {where}: oracle {oracle:R}, engine {engine:R}";
            }

            // a zero is +0.0 on both sides (ADR 0038 #13, ADR 0047 R1): -0.0 against +0.0 is a difference of 0 and is still a mismatch
            return difference > 0 && difference <= Tolerance;
        }
    }
}
