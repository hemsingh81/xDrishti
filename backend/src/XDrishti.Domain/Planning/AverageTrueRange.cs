namespace XDrishti.Domain.Planning;

/// <summary>The plain average of the last <c>period</c> true ranges of a series of closed sessions (spec P7-11): the true range is the larger of high minus low and the distance of high and of low from the previous close.</summary>
public static class AverageTrueRange
{
    public const int DefaultPeriod = 14;

    /// <summary>Null when there are fewer than <c>period</c> + 1 sessions or a value is not a usable number.</summary>
    public static double? Of(IReadOnlyList<DailyRange> sessions, int period = DefaultPeriod)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentOutOfRangeException.ThrowIfLessThan(period, 1);
        if (sessions.Count < period + 1)
        {
            return null;
        }

        var sum = 0.0;
        for (var i = sessions.Count - period; i < sessions.Count; i++)
        {
            var bar = sessions[i];
            var before = sessions[i - 1].Close;
            if (!double.IsFinite(bar.High + bar.Low + before) || bar.High < bar.Low || before <= 0)
            {
                return null;
            }

            sum += Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - before), Math.Abs(bar.Low - before)));
        }

        var atr = sum / period;
        return atr > 0 ? atr : null;
    }
}
