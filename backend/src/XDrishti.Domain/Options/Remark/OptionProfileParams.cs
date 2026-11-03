using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The parameters of an <c>option_buy</c> expression profile (spec P14-08), and their canonical text through <see cref="ExpressionProfile"/>: <c>strike</c> (<c>atm</c> or <c>itm1</c>), <c>expiry</c> (<c>nearest_safe</c>), <c>premium_stop_pct</c> (<c>none</c> or a percent of the
/// entry premium above 0 and below 100) and <c>max_hold_sessions</c> (the planned last session of a swing buy, entry session counted as 1; a sell is always intraday). All four are always written, so one choice has one hash. A <c>future</c> profile is refused:
/// no futures bars exist.
/// </summary>
public sealed record OptionProfileParams(string Strike, string Expiry, decimal? PremiumStopPct, int MaxHoldSessions)
{
    public const string Atm = "atm";
    public const string Itm1 = "itm1";
    public const string NearestSafe = "nearest_safe";
    public const string None = "none";
    public const string InvalidCode = "option_profile.invalid";
    public const string NoFuturesBarsCode = "option_profile.no_futures_bars";
    public const int MaxHold = 20;

    /// <summary>Makes the parameters; refuses (does not throw) a value outside the closed lists.</summary>
    public static Result<OptionProfileParams> Create(string strike, string expiry, decimal? premiumStopPct, int maxHoldSessions)
    {
        if (strike is not (Atm or Itm1))
        {
            return Invalid("strike is atm or itm1");
        }

        if (expiry != NearestSafe)
        {
            return Invalid("expiry is nearest_safe");
        }

        if (premiumStopPct is <= 0 or >= 100)
        {
            return Invalid("premium_stop_pct is above 0 and below 100, or none");
        }

        return maxHoldSessions is < 1 or > MaxHold ? Invalid($"max_hold_sessions is from 1 to {MaxHold}") : Result.Success(new OptionProfileParams(strike, expiry, premiumStopPct, maxHoldSessions));
    }

    /// <summary>The profile these parameters register.</summary>
    public Result<ExpressionProfile> ToProfile() => ExpressionProfile.Create(
        Expression.OptionBuy,
        [
            new("strike", Strike),
            new("expiry", Expiry),
            new("premium_stop_pct", PremiumStopPct is { } p ? (object)p : None),
            new("max_hold_sessions", MaxHoldSessions),
        ]);

    /// <summary>Reads the parameters of a stored profile; refuses a <c>future</c> profile (<see cref="NoFuturesBarsCode"/>), any other kind, and a parameter list that is not exactly the four.</summary>
    public static Result<OptionProfileParams> FromProfile(ExpressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind == Expression.Future)
        {
            return Result.Failure<OptionProfileParams>(Error.Validation(NoFuturesBarsCode, "A future profile cannot be re-marked: no futures bars exist yet."));
        }

        if (profile.Kind != Expression.OptionBuy)
        {
            return Invalid("must be of kind option_buy");
        }

        var values = profile.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (values.Count != 4 || !values.TryGetValue("strike", out var strike) || !values.TryGetValue("expiry", out var expiry) || !values.TryGetValue("premium_stop_pct", out var stop) || !values.TryGetValue("max_hold_sessions", out var hold))
        {
            return Invalid("has exactly strike, expiry, premium_stop_pct and max_hold_sessions");
        }

        decimal? pct = null;
        if (stop != None)
        {
            if (!decimal.TryParse(stop, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return Invalid("premium_stop_pct is a number or none");
            }

            pct = parsed;
        }

        return int.TryParse(hold, NumberStyles.None, CultureInfo.InvariantCulture, out var sessions) ? Create(strike, expiry, pct, sessions) : Invalid("max_hold_sessions is a whole number");
    }

    private static Result<OptionProfileParams> Invalid(string rule) => Result.Failure<OptionProfileParams>(Error.Validation(InvalidCode, "An option_buy profile " + rule + "."));
}
