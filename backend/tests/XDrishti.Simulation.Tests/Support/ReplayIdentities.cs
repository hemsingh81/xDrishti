using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The identities of the record of a replay (spec P3-04, Scope 8, criterion 12), which hold on every fixture.</summary>
internal static class ReplayIdentities
{
    public static void ShouldHold(InstrumentReplay replay, string name = "")
    {
        var r = replay.Record;
        var candidates = r.Scans.Sum(s => s.Candidates);

        (r.BarsFed + r.BarsExcluded).ShouldBe(r.BarsRead, name + ": bars read = bars fed + bars excluded");
        r.BarsExcluded.ShouldBeGreaterThanOrEqualTo(r.BarsOutsideSession, name);
        r.Fates.Total.ShouldBe(candidates, name + ": the sum of the fates = the scanner's candidates, both sides, all segments");
        r.Walk.Candidates.ShouldBe(candidates, name + ": the walker walked every candidate");
        r.Scans.ShouldAllBe(s => s.IgnoredBars == 0, name + ": IgnoredBars is 0 on stored data");
        r.Notes.Count.ShouldBe((int)Math.Min(r.Sessions.Excluded, InstrumentRun.MaxNotes), name);
        r.NotesTruncated.ShouldBe(r.Sessions.Excluded > InstrumentRun.MaxNotes, name);
        r.CandidatesInWindow.ShouldBeLessThanOrEqualTo(candidates, name);
        if (r.Skipped is null)
        {
            r.Fates.Trades.ShouldBe(replay.Trades.Count, name);
            r.Fates.OpenAtEnd.ShouldBe(replay.OpenAtEnd.Count, name);
            r.TradesInWindow.ShouldBe(replay.Trades.Count + replay.OpenAtEnd.Count, name);
            replay.Trades.ShouldAllBe(t => t.ExitReason != ExitReason.EndOfData, name);
            replay.OpenAtEnd.ShouldAllBe(t => t.ExitReason == ExitReason.EndOfData, name);
        }
        else
        {
            replay.Trades.ShouldBeEmpty(name);
            replay.OpenAtEnd.ShouldBeEmpty(name);
        }
    }
}
