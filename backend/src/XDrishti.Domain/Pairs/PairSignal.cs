namespace XDrishti.Domain.Pairs;

/// <summary>
/// The entry, exit and stop state machine (spec P14-12, protocol S.3). Flat: <c>long_spread</c> when <c>z &lt;= -entry</c>, <c>short_spread</c> when <c>z &gt;= entry</c>. In a position: <c>stop</c> when <c>|z| &gt;= stop</c> (checked first), else <c>exit</c> when <c>|z| &lt;= exit</c>.
/// An unavailable z never opens and never closes by rule: the step holds with <see cref="PairsReason.ZUnavailable"/> so the caller counts it; the caller's session square-off still applies. Pure: the state is passed in and returned.
/// </summary>
public static class PairSignal
{
    public static SignalStep Next(PairPosition position, PairsResult<double> z, PairsOptions? options = null)
    {
        options ??= PairsOptions.Default;
        if (!options.IsValid || !Enum.IsDefined(position))
        {
            return new SignalStep(SignalAction.Hold, position, PairsReason.BadInput);
        }

        if (!z.TryGet(out var value))
        {
            return new SignalStep(SignalAction.Hold, position, PairsReason.ZUnavailable);
        }

        if (position == PairPosition.Flat)
        {
            if (value <= -options.Entry)
            {
                return new SignalStep(SignalAction.EnterLongSpread, PairPosition.LongSpread, null);
            }

            return value >= options.Entry
                ? new SignalStep(SignalAction.EnterShortSpread, PairPosition.ShortSpread, null)
                : new SignalStep(SignalAction.Hold, PairPosition.Flat, null);
        }

        var abs = Math.Abs(value);
        if (abs >= options.Stop)
        {
            return new SignalStep(SignalAction.Stop, PairPosition.Flat, null);
        }

        return abs <= options.Exit
            ? new SignalStep(SignalAction.Exit, PairPosition.Flat, null)
            : new SignalStep(SignalAction.Hold, position, null);
    }
}
