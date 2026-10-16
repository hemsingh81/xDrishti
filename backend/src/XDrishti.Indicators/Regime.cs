using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Indicators;

/// <summary>
/// The names of the nine market regimes of doc 07 §6 (trend × volatility), as the strategy language and the reports spell them: <c>UP_LOW_VOL</c> (1), <c>UP_NORMAL_VOL</c> (2), <c>UP_HIGH_VOL</c> (3),
/// <c>RANGE_LOW_VOL</c> (4) … <c>RANGE_HIGH_VOL</c> (6), <c>DOWN_LOW_VOL</c> (7) … <c>DOWN_HIGH_VOL</c> (9). The code of a day is <c>(1 − regime_trend) × 3 + regime_vol + 1</c> with the trend +1, 0, −1 and the
/// volatility 0, 1, 2 (see <see cref="ContextValues"/>); <see cref="Name"/> and <see cref="Code"/> are inverses over 1 to 9.
/// </summary>
public static class Regime
{
    /// <summary>The problem code of <see cref="Code"/> for a name that is none of the nine.</summary>
    public const string UnknownNameCode = "indicators.unknown_regime";

    private static readonly string[] Labels =
    [
        "UP_LOW_VOL", "UP_NORMAL_VOL", "UP_HIGH_VOL",
        "RANGE_LOW_VOL", "RANGE_NORMAL_VOL", "RANGE_HIGH_VOL",
        "DOWN_LOW_VOL", "DOWN_NORMAL_VOL", "DOWN_HIGH_VOL",
    ];

    /// <summary>The name of a regime code from 1 to 9. A code outside that range is a programming error (a not-ready regime is NaN and must be checked before it becomes a code) and throws.</summary>
    public static string Name(int code) =>
        code is >= 1 and <= 9 ? Labels[code - 1] : throw new ArgumentOutOfRangeException(nameof(code), code, "A regime code is 1 (UP_LOW_VOL) to 9 (DOWN_HIGH_VOL).");

    /// <summary>The code 1 to 9 of a regime name. The spelling is exact and case-sensitive (<c>UP_LOW_VOL</c>); anything else is a validation error with the code <see cref="UnknownNameCode"/>.</summary>
    public static Result<int> Code(string name)
    {
        for (var i = 0; i < Labels.Length; i++)
        {
            if (string.Equals(Labels[i], name, StringComparison.Ordinal))
            {
                return Result.Success(i + 1);
            }
        }

        return Result.Failure<int>(Error.Validation(UnknownNameCode, string.Create(CultureInfo.InvariantCulture, $"'{name}' is not a market regime; use one of {string.Join(", ", Labels)}.")));
    }
}
