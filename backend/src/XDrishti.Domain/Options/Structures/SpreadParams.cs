using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The parameters of a <c>debit_spread</c> or <c>credit_spread</c> expression profile (spec P14-10), and their canonical text through <see cref="ExpressionProfile"/>: <c>strike</c> (<c>atm</c> or <c>itm1</c>, the P14-08 rule for the first leg), <c>width</c> (2 or 4 strike steps),
/// <c>expiry</c> (<c>nearest_safe</c>), <c>structure_stop_pct</c> (<c>none</c> or a percent of the entry debit above 0 and below 100; a credit spread has none: its loss is capped by its width) and <c>max_hold_sessions</c> (1 to 20). All five are always written, so one choice has one hash.
/// A debit spread on a share is limited to width 2 (the shares' rolling window is +-3 offsets), a credit spread to an index: <see cref="ValidateFor"/>.
/// </summary>
public sealed record SpreadParams(Expression Kind, string Strike, int WidthSteps, string Expiry, decimal? StopPct, int MaxHoldSessions)
{
    public const string InvalidCode = "structure_profile.invalid";
    public const string WidthOutsideWindowCode = "width_outside_window";
    public const string IndexOnlyCode = "structure_profile.index_only";

    /// <summary>The widest a share's spread can be: the window is +-3 offsets, the first leg is one step in the money at most.</summary>
    public const int ShareMaxWidth = 2;

    public static Result<SpreadParams> Create(Expression kind, string strike, int widthSteps, string expiry, decimal? stopPct, int maxHoldSessions)
    {
        if (kind is not (Expression.DebitSpread or Expression.CreditSpread))
        {
            return Invalid("must be of kind debit_spread or credit_spread");
        }

        if (strike is not (OptionProfileParams.Atm or OptionProfileParams.Itm1))
        {
            return Invalid("strike is atm or itm1");
        }

        if (widthSteps is not (2 or 4))
        {
            return Invalid("width is 2 or 4 strike steps");
        }

        if (expiry != OptionProfileParams.NearestSafe)
        {
            return Invalid("expiry is nearest_safe");
        }

        if (stopPct is <= 0 or >= 100)
        {
            return Invalid("structure_stop_pct is above 0 and below 100, or none");
        }

        if (kind == Expression.CreditSpread && stopPct is not null)
        {
            return Invalid("structure_stop_pct is none for a credit spread (its loss is capped by its width)");
        }

        return maxHoldSessions is < 1 or > OptionProfileParams.MaxHold
            ? Invalid($"max_hold_sessions is from 1 to {OptionProfileParams.MaxHold}")
            : Result.Success(new SpreadParams(kind, strike, widthSteps, expiry, stopPct, maxHoldSessions));
    }

    public Result<ExpressionProfile> ToProfile() => ExpressionProfile.Create(
        Kind,
        [
            new("strike", Strike),
            new("width", WidthSteps),
            new("expiry", Expiry),
            new("structure_stop_pct", StopPct is { } p ? (object)p : OptionProfileParams.None),
            new("max_hold_sessions", MaxHoldSessions),
        ]);

    /// <summary>Reads the parameters of a stored profile; refuses any other kind and a parameter list that is not exactly the five.</summary>
    public static Result<SpreadParams> FromProfile(ExpressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind is not (Expression.DebitSpread or Expression.CreditSpread))
        {
            return Invalid("must be of kind debit_spread or credit_spread");
        }

        var values = profile.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (values.Count != 5 || !values.TryGetValue("strike", out var strike) || !values.TryGetValue("width", out var width) || !values.TryGetValue("expiry", out var expiry)
            || !values.TryGetValue("structure_stop_pct", out var stop) || !values.TryGetValue("max_hold_sessions", out var hold))
        {
            return Invalid("has exactly strike, width, expiry, structure_stop_pct and max_hold_sessions");
        }

        decimal? pct = null;
        if (stop != OptionProfileParams.None)
        {
            if (!decimal.TryParse(stop, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return Invalid("structure_stop_pct is a number or none");
            }

            pct = parsed;
        }

        if (!int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out var steps) || !int.TryParse(hold, NumberStyles.None, CultureInfo.InvariantCulture, out var sessions))
        {
            return Invalid("width and max_hold_sessions are whole numbers");
        }

        return Create(profile.Kind, strike, steps, expiry, pct, sessions);
    }

    /// <summary>Whether the structure can be built on the underlying: a width of 4 does not fit a share's window (<see cref="WidthOutsideWindowCode"/>) and a credit spread is for an index (<see cref="IndexOnlyCode"/>). Refused at profile validation, before any bar is read.</summary>
    public Result ValidateFor(string canonicalSymbol)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        var index = canonicalSymbol is ExpiryRules.Nifty or ExpiryRules.BankNifty;
        if (!index && Kind == Expression.CreditSpread)
        {
            return Result.Failure(Error.Validation(IndexOnlyCode, "A credit spread is built on an index option only."));
        }

        return !index && WidthSteps > ShareMaxWidth
            ? Result.Failure(Error.Validation(WidthOutsideWindowCode, $"A width of {WidthSteps} steps does not fit the +-3 offsets of a share: the wings cannot be priced."))
            : Result.Success();
    }

    private static Result<SpreadParams> Invalid(string rule) => Result.Failure<SpreadParams>(Error.Validation(InvalidCode, "A debit or credit spread profile " + rule + "."));
}
