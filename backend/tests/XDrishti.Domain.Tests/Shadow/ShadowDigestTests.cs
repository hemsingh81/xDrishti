using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Tests.Shadow;

/// <summary>CF-08 criterion 4 (domain half): the chain digest and its recomputation, with a negative control for every kind of tampering.</summary>
public sealed class ShadowDigestTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static ShadowEntry Entry(DateOnly session, string symbol) =>
        new(session, 1, symbol.GetHashCode(StringComparison.Ordinal), $"ORB|{symbol}|Buy|{session:yyyy-MM-dd}", "primary", "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", new string('a', 64), symbol, "BUY", 100m, 98m, 104m, "09:30-11:30", null, null, null, null, null, true, 1);

    private static ShadowChainSession Session(DateOnly date, string? prev, params ShadowEntry[] entries)
    {
        var digest = ShadowDigest.Next(prev, entries.Select(e => e.RowHash));
        return new ShadowChainSession(date, "ok", prev, digest, [.. entries.Select(e => new StoredShadowEntry(e, e.RowHash))]);
    }

    private static List<ShadowChainSession> Week()
    {
        var one = Session(Monday, null, Entry(Monday, "A"), Entry(Monday, "B"));
        var two = Session(Monday.AddDays(1), one.Digest, Entry(Monday.AddDays(1), "C"));
        var three = Session(Monday.AddDays(2), two.Digest, Entry(Monday.AddDays(2), "D"), Entry(Monday.AddDays(2), "E"), Entry(Monday.AddDays(2), "F"));
        return [one, two, three];
    }

    [Fact]
    public void The_digest_is_64_hex_characters_and_does_not_depend_on_the_order_of_the_rows()
    {
        var a = ShadowDigest.Next(null, ["bb", "aa", "cc"]);
        var b = ShadowDigest.Next(null, ["cc", "aa", "bb"]);

        a.ShouldBe(b);
        a.Length.ShouldBe(64);
        a.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void The_digest_depends_on_the_previous_digest_and_on_every_row()
    {
        var baseline = ShadowDigest.Next("p", ["a", "b"]);

        ShadowDigest.Next("q", ["a", "b"]).ShouldNotBe(baseline);
        ShadowDigest.Next("p", ["a"]).ShouldNotBe(baseline);
        ShadowDigest.Next("p", ["a", "b", "c"]).ShouldNotBe(baseline);
        ShadowDigest.Next(null, []).ShouldNotBe(ShadowDigest.Next("p", []));
    }

    [Fact]
    public void A_week_recomputed_from_its_stored_rows_matches()
    {
        ShadowDigest.FirstBroken(Week()).ShouldBeNull();
    }

    [Fact]
    public void Changing_one_stored_column_breaks_the_chain_from_that_session()
    {
        var week = Week();
        var tampered = week[1].Entries[0] with { Entry = week[1].Entries[0].Entry with { Stop = 90m } };
        week[1] = week[1] with { Entries = [tampered] };

        ShadowDigest.FirstBroken(week).ShouldBe(Monday.AddDays(1));
    }

    [Fact]
    public void Changing_a_stored_row_hash_together_with_the_column_still_breaks_the_digest()
    {
        var week = Week();
        var changed = week[1].Entries[0].Entry with { Symbol = "ZZZ" };
        week[1] = week[1] with { Entries = [new StoredShadowEntry(changed, changed.RowHash)] }; // the row is self-consistent, the session digest is not

        ShadowDigest.FirstBroken(week).ShouldBe(Monday.AddDays(1));
    }

    [Fact]
    public void A_deleted_row_and_an_inserted_row_break_the_chain()
    {
        var deleted = Week();
        deleted[0] = deleted[0] with { Entries = [deleted[0].Entries[0]] };
        var inserted = Week();
        var extra = Entry(Monday.AddDays(2), "G");
        inserted[2] = inserted[2] with { Entries = [.. inserted[2].Entries, new StoredShadowEntry(extra, extra.RowHash)] };

        ShadowDigest.FirstBroken(deleted).ShouldBe(Monday);
        ShadowDigest.FirstBroken(inserted).ShouldBe(Monday.AddDays(2));
    }

    [Fact]
    public void A_removed_session_breaks_the_link_of_the_next_one()
    {
        var week = Week();
        week.RemoveAt(1);

        ShadowDigest.FirstBroken(week).ShouldBe(Monday.AddDays(2));
    }

    [Fact]
    public void A_gap_is_a_link_of_the_chain_with_no_rows()
    {
        var one = Session(Monday, null, Entry(Monday, "A"));
        var gap = new ShadowChainSession(Monday.AddDays(1), "gap", one.Digest, ShadowDigest.Next(one.Digest, []), []);
        var three = Session(Monday.AddDays(2), gap.Digest, Entry(Monday.AddDays(2), "D"));

        ShadowDigest.FirstBroken([one, gap, three]).ShouldBeNull();
        ShadowDigest.FirstBroken([one, three]).ShouldBe(Monday.AddDays(2));
    }
}
