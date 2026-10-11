namespace XDrishti.Application.Portfolio.Returns;

/// <summary>The outcome of an XIRR calculation. <see cref="Rate"/> is a fraction per year (0.161 = 16.1 %) and only set when <see cref="State"/> is <see cref="XirrState.Ok"/> or <see cref="XirrState.NotMeaningful"/>.</summary>
public sealed record XirrResult(XirrState State, double? Rate)
{
    public static readonly XirrResult NoData = new(XirrState.NoData, null);

    public static readonly XirrResult Insufficient = new(XirrState.Insufficient, null);
}

/// <summary>
/// Annualised internal rate of return of dated cash flows, the way a spreadsheet's XIRR defines it: the rate r with
/// Σ amountᵢ / (1 + r)^((dateᵢ − date₀) / 365) = 0, where date₀ is the first day. Newton–Raphson from a good first guess; when it does not
/// converge, bisection on [−0.9999, 100]. Pure and deterministic (docs/design/12 §4, ADR 0012).
/// </summary>
public static class Xirr
{
    /// <summary>Below this many days between the first and the last cash flow no rate is given.</summary>
    public const int MinDays = 30;

    public const double Lower = -0.9999;

    public const double Upper = 100.0;

    public const double Tolerance = 1e-9;

    /// <summary>Rates beyond ±1,000 % (±10 as a fraction) are not meaningful.</summary>
    public const double NotMeaningfulBeyond = 10.0;

    private const int MaxNewtonIterations = 100;

    private const int MaxBisectionIterations = 300;

    private const int ScanSteps = 400;

    private const double DaysPerYear = 365.0;

    public static XirrResult Compute(IReadOnlyList<CashFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        if (flows.Count == 0 || flows.All(f => f.Amount == 0m))
        {
            return XirrResult.NoData;
        }

        var hasOut = flows.Any(f => f.Amount < 0m);
        var hasIn = flows.Any(f => f.Amount > 0m);
        if (!hasOut || !hasIn)
        {
            return XirrResult.Insufficient;
        }

        var first = flows.Min(f => f.Date);
        var last = flows.Max(f => f.Date);
        if (last.DayNumber - first.DayNumber < MinDays)
        {
            return XirrResult.Insufficient;
        }

        var points = flows
            .Where(f => f.Amount != 0m)
            .Select(f => (Years: (f.Date.DayNumber - first.DayNumber) / DaysPerYear, Amount: (double)f.Amount))
            .ToArray();

        var rate = Solve(points, Guess(points));
        if (rate is not { } found)
        {
            return RootBeyondUpper(points) ? new XirrResult(XirrState.NotMeaningful, null) : XirrResult.Insufficient;
        }

        return Math.Abs(found) > NotMeaningfulBeyond ? new XirrResult(XirrState.NotMeaningful, found) : new XirrResult(XirrState.Ok, found);
    }

    /// <summary>
    /// True when the root lies above <see cref="Upper"/>: as the rate grows, the value tends to the first flow, so if it still has the other sign at the
    /// upper bound, it crosses zero further up. Such a rate is far beyond ±1,000 % (not meaningful), only the solver does not search there.
    /// </summary>
    private static bool RootBeyondUpper((double Years, double Amount)[] points)
    {
        var firstYears = points.Min(p => p.Years);
        var first = points.Where(p => p.Years == firstYears).Sum(p => p.Amount);
        var atUpper = Npv(points, Upper);
        return double.IsFinite(atUpper) && first != 0.0 && Math.Sign(atUpper) == -Math.Sign(first);
    }

    /// <summary>The net present value at rate <paramref name="rate"/> of flows given as (years from the first day, amount).</summary>
    internal static double Npv((double Years, double Amount)[] points, double rate)
    {
        var sum = 0.0;
        var growth = 1.0 + rate;
        foreach (var (years, amount) in points)
        {
            sum += amount * Math.Pow(growth, -years);
        }

        return sum;
    }

    private static double Derivative((double Years, double Amount)[] points, double rate)
    {
        var sum = 0.0;
        var growth = 1.0 + rate;
        foreach (var (years, amount) in points)
        {
            sum -= years * amount * Math.Pow(growth, -years - 1.0);
        }

        return sum;
    }

    /// <summary>First guess: the growth of everything paid in into everything paid out over the money-weighted period.</summary>
    private static double Guess((double Years, double Amount)[] points)
    {
        var paidIn = points.Where(p => p.Amount < 0).Sum(p => -p.Amount);
        var received = points.Where(p => p.Amount > 0).Sum(p => p.Amount);
        var weightedYears = points.Where(p => p.Amount < 0).Sum(p => -p.Amount * p.Years) / paidIn;
        var years = Math.Max(points.Where(p => p.Amount > 0).Sum(p => p.Amount * p.Years) / received - weightedYears, 1.0 / DaysPerYear);
        var guess = Math.Pow(received / paidIn, 1.0 / years) - 1.0;
        return double.IsFinite(guess) ? Math.Clamp(guess, -0.99, 10.0) : 0.1;
    }

    private static double? Solve((double Years, double Amount)[] points, double guess)
    {
        var scale = points.Sum(p => Math.Abs(p.Amount));
        return Newton(points, guess, scale) ?? Bisection(points, guess);
    }

    private static double? Newton((double Years, double Amount)[] points, double start, double scale)
    {
        var rate = start;
        for (var i = 0; i < MaxNewtonIterations; i++)
        {
            var value = Npv(points, rate);
            var slope = Derivative(points, rate);
            if (!double.IsFinite(value) || !double.IsFinite(slope) || slope == 0.0)
            {
                return null;
            }

            var next = rate - (value / slope);
            if (next <= Lower)
            {
                next = (rate + Lower) / 2.0;
            }
            else if (next > Upper)
            {
                next = (rate + Upper) / 2.0;
            }

            if (Math.Abs(next - rate) <= Tolerance)
            {
                return IsRoot(points, next, scale) ? next : null;
            }

            rate = next;
        }

        return null;
    }

    /// <summary>Bisection on [−0.9999, 100]; when the ends have the same sign, on the bracket found by scanning that lies closest to the guess.</summary>
    private static double? Bisection((double Years, double Amount)[] points, double guess)
    {
        var low = Lower;
        var high = Upper;
        var fLow = Npv(points, low);
        var fHigh = Npv(points, high);
        if (!double.IsFinite(fLow) || !double.IsFinite(fHigh))
        {
            return null;
        }

        if (Math.Sign(fLow) == Math.Sign(fHigh))
        {
            var bracket = Scan(points, guess);
            if (bracket is not { } found)
            {
                return null;
            }

            (low, high) = found;
            fLow = Npv(points, low);
        }

        for (var i = 0; i < MaxBisectionIterations && high - low > Tolerance / 10.0; i++)
        {
            var middle = (low + high) / 2.0;
            var fMiddle = Npv(points, middle);
            if (!double.IsFinite(fMiddle))
            {
                return null;
            }

            if (fMiddle == 0.0)
            {
                return middle;
            }

            if (Math.Sign(fMiddle) == Math.Sign(fLow))
            {
                low = middle;
                fLow = fMiddle;
            }
            else
            {
                high = middle;
            }
        }

        // The bracket kept a sign change and collapsed: a root of a continuous function.
        return (low + high) / 2.0;
    }

    /// <summary>The sign change between neighbouring points of a grid (even in ln(1 + r)) that lies closest to the guess.</summary>
    private static (double Low, double High)? Scan((double Years, double Amount)[] points, double guess)
    {
        var lnLow = Math.Log(1.0 + Lower);
        var lnHigh = Math.Log(1.0 + Upper);
        (double Low, double High)? best = null;
        var bestDistance = double.MaxValue;
        var previousRate = Lower;
        var previousValue = Npv(points, previousRate);
        for (var i = 1; i <= ScanSteps; i++)
        {
            var rate = Math.Exp(lnLow + ((lnHigh - lnLow) * i / ScanSteps)) - 1.0;
            var value = Npv(points, rate);
            if (double.IsFinite(previousValue) && double.IsFinite(value) && (value == 0.0 || Math.Sign(value) != Math.Sign(previousValue)))
            {
                var distance = Math.Abs(((previousRate + rate) / 2.0) - guess);
                if (distance < bestDistance)
                {
                    best = (previousRate, rate);
                    bestDistance = distance;
                }
            }

            previousRate = rate;
            previousValue = value;
        }

        return best;
    }

    private static bool IsRoot((double Years, double Amount)[] points, double rate, double scale)
    {
        var value = Npv(points, rate);
        return double.IsFinite(value) && Math.Abs(value) <= 1e-6 * scale;
    }
}
