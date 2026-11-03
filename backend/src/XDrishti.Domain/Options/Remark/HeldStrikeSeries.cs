namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The premium series of one fixed strike rebuilt from the rolling offsets (spec P14-08): at each bar time the held bar is the bar of whichever offset has that <c>strike_px</c> at that time, so the premium follows the strike while the spot moves it across the offsets.
/// Where no offset has it (the strike left the window) or the bar is suspect, the state is <see cref="HeldState.Missing"/>; a bar from an unusable key-session is <see cref="HeldState.Unusable"/>. A cheap view over a shared <see cref="BarGrid"/>. Pure.
/// </summary>
public sealed class HeldStrikeSeries(BarGrid grid, int strikePx)
{
    public int StrikePx { get; } = strikePx;

    public BarGrid Grid { get; } = grid;

    /// <summary>The state of the held strike at the bar time and, when <see cref="HeldState.Available"/>, its bar.</summary>
    public HeldState Held(DateTimeOffset at, out OptionBar? bar)
    {
        bar = null;
        var any = false;
        var unusable = false;
        foreach (var candidate in Grid.At(at))
        {
            if (candidate.StrikePx != StrikePx)
            {
                continue;
            }

            any = true;
            if (candidate.Usable && !candidate.Suspect)
            {
                bar = candidate;
                return HeldState.Available;
            }

            unusable |= !candidate.Usable;
        }

        return any && unusable ? HeldState.Unusable : HeldState.Missing;
    }

    /// <summary>
    /// The latest vendor IV of the held strike on a usable, unsuspected bar strictly earlier than <paramref name="before"/> and no more than <paramref name="maxAgeMinutes"/> before it; null when there is none. Never a bar at or after <paramref name="before"/>.
    /// </summary>
    public (float Iv, DateTimeOffset Ts)? LatestIv(DateTimeOffset before, int maxAgeMinutes) => LatestIv(before, before, maxAgeMinutes);

    /// <summary>As above, with the age measured from <paramref name="ageFrom"/> (the instant the model price is for) instead of the bar time.</summary>
    public (float Iv, DateTimeOffset Ts)? LatestIv(DateTimeOffset before, DateTimeOffset ageFrom, int maxAgeMinutes)
    {
        var limit = before.ToUniversalTime();
        var from = ageFrom.ToUniversalTime();
        var start = Grid.FirstAtOrAfter(limit);
        for (var i = (start < 0 ? Grid.Times.Count : start) - 1; i >= 0; i--)
        {
            var time = Grid.Times[i];
            if ((from - time).TotalMinutes > maxAgeMinutes)
            {
                return null;
            }

            if (Held(time, out var bar) == HeldState.Available && bar!.Iv is > 0)
            {
                return (bar.Iv.Value, time);
            }
        }

        return null;
    }
}
