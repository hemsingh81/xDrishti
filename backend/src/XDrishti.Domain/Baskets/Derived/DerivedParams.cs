using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Baskets.Derived;

/// <summary>
/// The versioned numbers of the derived baskets: every default lives here (never tuned on results), overrides are checked at construction,
/// and <c>Hash</c> is the SHA-256 of the canonical JSON (sorted keys), so any change of a number or of the version changes it.
/// </summary>
public sealed class DerivedParams
{
    private static readonly SortedDictionary<string, double> Defaults = new(StringComparer.Ordinal)
    {
        ["BufferFactor"] = 1.5,
        ["MinDwellSessions"] = 5,
        ["Basket1.TopK"] = 50, ["Basket1.AtrWindow"] = 14, ["Basket1.RvWindow"] = 20, ["Basket1.MinBars"] = 60,
        ["Basket2.TopK"] = 30, ["Basket2.Window"] = 5, ["Basket2.Variant"] = 0, ["Basket2.MinBars"] = 25,
        ["Basket3.TopK"] = 100, ["Basket3.Window"] = 20, ["Basket3.MinBars"] = 60,
        ["Basket4.Cap"] = 30, ["Basket4.GapFloor"] = 0.02, ["Basket4.AtrMult"] = 1.5, ["Basket4.AtrWindow"] = 14, ["Basket4.VolWindow"] = 20, ["Basket4.MinBars"] = 40,
        ["Basket5.TopK"] = 50, ["Basket5.Window"] = 250, ["Basket5.MinBeta"] = 1, ["Basket5.MinBars"] = 200,
        ["Basket6.TopK"] = 30, ["Basket6.Variant"] = 0, ["Basket6.Skip"] = 21, ["Basket6.LongWindow"] = 252, ["Basket6.ShortWindow"] = 126, ["Basket6.MinBars"] = 252, ["Basket6.MinBars6m"] = 126,
        ["Basket7.TopK"] = 50, ["Basket7.Window"] = 250, ["Basket7.Lag"] = 5, ["Basket7.MaxVr"] = 0.85, ["Basket7.MaxRho1"] = -0.05, ["Basket7.MaxEr"] = 0.3, ["Basket7.ErWindow"] = 20, ["Basket7.MinBars"] = 200,
        ["Basket8.TopK"] = 50, ["Basket8.ErWindow"] = 20, ["Basket8.MinEr"] = 0.5, ["Basket8.AdxWindow"] = 14, ["Basket8.MinAdx"] = 25, ["Basket8.MinBars"] = 60,
        ["Basket9.Cap"] = 50, ["Basket9.Window"] = 252, ["Basket9.MinRatio"] = 0.97, ["Basket9.AdtvFloor"] = 200_000_000, ["Basket9.SmaWindow"] = 200, ["Basket9.MinBars"] = 252,
        ["Basket10.TopK"] = 30, ["Basket10.Window"] = 252, ["Basket10.AdtvTop"] = 200, ["Basket10.AdtvWindow"] = 20, ["Basket10.MinBars"] = 252,
        ["Basket11.MinRvol"] = 2, ["Basket11.MinPrice"] = 50, ["Basket11.Window"] = 20, ["Basket11.MinBars"] = 40,
        ["Basket12.TopK"] = 30, ["Basket12.Window"] = 20, ["Basket12.WideMult"] = 1.5, ["Basket12.MinOrPct"] = 0.008, ["Basket12.OrMinutes"] = 15, ["Basket12.MinBars"] = 15,
        ["Basket13.MinAdtv"] = 100_000_000, ["Basket13.MaxZeroVolFraction"] = 0.05, ["Basket13.TickMult"] = 3, ["Basket13.Window"] = 20, ["Basket13.MinBars"] = 15,
        ["Basket14.Clusters"] = 12, ["Basket14.MinSize"] = 5, ["Basket14.Window"] = 120, ["Basket14.MinBars"] = 100,
    };

    private readonly SortedDictionary<string, double> _values;

    private DerivedParams(int version, SortedDictionary<string, double> values)
    {
        Version = version;
        _values = values;
        Hash = ComputeHash(version, values);
    }

    public int Version { get; }

    public string Hash { get; }

    public IReadOnlyDictionary<string, double> Values => _values;

    public double this[string key] => _values[key];

    public int Whole(string key) => (int)_values[key];

    /// <summary>The defaults plus the given overrides. Fails with a message naming the key when one is unknown or out of range.</summary>
    public static Result<DerivedParams> Create(int version, IReadOnlyDictionary<string, double>? overrides = null)
    {
        if (version < 1)
        {
            return Result.Failure<DerivedParams>(Error.Validation("derived.version", "The version must be 1 or more."));
        }

        var values = new SortedDictionary<string, double>(Defaults, StringComparer.Ordinal);
        foreach (var (key, value) in overrides ?? new Dictionary<string, double>())
        {
            if (!values.ContainsKey(key))
            {
                return Result.Failure<DerivedParams>(Error.Validation("derived.unknown_key", $"Unknown parameter '{key}'."));
            }

            values[key] = value;
        }

        foreach (var (key, value) in values)
        {
            var problem = Check(key, value);
            if (problem is not null)
            {
                return Result.Failure<DerivedParams>(Error.Validation("derived.invalid_value", $"Parameter '{key}' {problem} (got {value.ToString(CultureInfo.InvariantCulture)})."));
            }
        }

        return Result.Success(new DerivedParams(version, values));
    }

    public static DerivedParams Default() => Create(1).Value;

    private static string? Check(string key, double value)
    {
        if (!double.IsFinite(value))
        {
            return "must be a finite number";
        }

        var name = key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..];
        if (name == "BufferFactor")
        {
            return value < 1 ? "must be at least 1" : null;
        }

        if (name is "TopK" or "Cap" or "Clusters" or "MinSize" or "AdtvTop")
        {
            return value < 1 ? "must be at least 1" : null;
        }

        if (name == "MinDwellSessions")
        {
            return value < 0 ? "must not be negative" : null;
        }

        if (name.EndsWith("Window", StringComparison.Ordinal) || name is "OrMinutes" or "MinBars" or "MinBars6m" or "Lag")
        {
            return value < 2 ? "must be at least 2" : null;
        }

        return name == "Variant" && value is not (0 or 1 or 2) ? "must be 0, 1 or 2" : null;
    }

    private static string ComputeHash(int version, SortedDictionary<string, double> values)
    {
        var json = new StringBuilder().Append("{\"Version\":").Append(version.ToString(CultureInfo.InvariantCulture)).Append(",\"Values\":{");
        var first = true;
        foreach (var (key, value) in values)
        {
            json.Append(first ? "" : ",").Append('"').Append(key).Append("\":").Append(value.ToString("R", CultureInfo.InvariantCulture));
            first = false;
        }

        json.Append("}}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json.ToString())));
    }
}
