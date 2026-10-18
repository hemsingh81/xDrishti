using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Simulation.Scanning;

/// <summary>Writes a feature the way the trace of a candidate names it: <c>close@15m</c>, <c>or_high(15)@15m</c>.</summary>
internal static class FeatureNames
{
    /// <summary>The name of the feature with its parameters in the order of the catalog, then <c>@</c> and the timeframe.</summary>
    public static string Of(in FeatureSpec spec)
    {
        var name = spec.Name;
        if (IndicatorCatalog.TryGet(spec.Name, out var info) && info.Parameters.Count > 0)
        {
            var arguments = new string[info.Parameters.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                arguments[i] = spec.Parameter(i).ToString("R", CultureInfo.InvariantCulture);
            }

            name = spec.Name + "(" + string.Join(", ", arguments) + ")";
        }

        return name + "@" + spec.Timeframe.ToCode();
    }
}
