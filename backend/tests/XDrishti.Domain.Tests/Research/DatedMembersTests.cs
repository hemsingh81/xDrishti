using XDrishti.Domain.Market;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>BK-11b, ADR 0266, criteria 2, 4, 5 and 6: the dated lookup, the structured basis, the key line and the words the views print.</summary>
public sealed class DatedMembersTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");

    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    private static DateTimeOffset At(int y, int m, int d) => new DateTimeOffset(new DateTime(y, m, d), IndiaTime.Offset);

    /// <summary>{A, B} until 2026-03-31, {A, C} from 2026-04-01 (the hand case of the spec).</summary>
    private static DatedMembers HandCase() => new(
    [
        (A, At(2026, 1, 1), null),
        (B, At(2026, 1, 1), At(2026, 4, 1)),
        (C, At(2026, 4, 1), null),
    ]);

    [Fact]
    public void The_members_of_the_hand_case_change_on_the_first_of_april()
    {
        var members = HandCase();

        members.On(new DateOnly(2026, 3, 31)).ShouldBe([A, B]);
        members.On(new DateOnly(2026, 4, 1)).ShouldBe([A, C]);
        members.IsMember(B, new DateOnly(2026, 4, 2)).ShouldBeFalse("a signal of B on 2026-04-02 is not taken");
        members.IsMember(C, new DateOnly(2026, 3, 30)).ShouldBeFalse("a signal of C on 2026-03-30 is not taken");
        members.IsMember(B, new DateOnly(2026, 3, 30)).ShouldBeTrue();
        members.IsMember(C, new DateOnly(2026, 4, 2)).ShouldBeTrue();
    }

    [Fact]
    public void An_instrument_that_is_not_in_the_history_is_never_a_member()
    {
        HandCase().IsMember(Guid.NewGuid(), new DateOnly(2026, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_stay_added_and_removed_on_one_day_is_never_a_member()
    {
        var members = new DatedMembers([(A, At(2026, 3, 3).AddHours(9), At(2026, 3, 3).AddHours(11))]);

        members.IsMember(A, new DateOnly(2026, 3, 3)).ShouldBeFalse();
        members.IsMemberDuring(A, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).ShouldBeFalse();
    }

    [Fact]
    public void A_history_of_one_day_of_change_is_not_dated_and_one_that_starts_after_the_window_is_not_dated_for_it()
    {
        new DatedMembers([(A, At(2026, 1, 1), null), (B, At(2026, 1, 1), null)]).IsDatedFor(new DateOnly(2026, 2, 1), out var onlyDay).ShouldBeFalse();
        onlyDay.ShouldBe(new DateOnly(2026, 1, 1));

        HandCase().IsDatedFor(new DateOnly(2025, 12, 1), out var first).ShouldBeFalse();
        first.ShouldBe(new DateOnly(2026, 1, 1));
        HandCase().IsDatedFor(new DateOnly(2026, 1, 1), out _).ShouldBeTrue();
        new DatedMembers([]).IsDatedFor(new DateOnly(2026, 1, 1), out var none).ShouldBeFalse();
        none.ShouldBeNull();
    }

    [Fact]
    public void The_hash_differs_for_different_histories_and_not_for_the_same_one_in_another_order()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 6, 30);
        var other = new DatedMembers([(A, At(2026, 1, 1), null), (B, At(2026, 1, 1), At(2026, 5, 1)), (C, At(2026, 5, 1), null)]);
        var reordered = new DatedMembers([(C, At(2026, 4, 1), null), (B, At(2026, 1, 1), At(2026, 4, 1)), (A, At(2026, 1, 1), null)]);

        HandCase().Hash(from, to).ShouldBe(reordered.Hash(from, to));
        HandCase().Hash(from, to).ShouldNotBe(other.Hash(from, to));
        HandCase().Hash(from, to).Length.ShouldBe(64);
    }

    [Fact]
    public void The_hash_ignores_what_lies_outside_the_window()
    {
        var narrow = new DatedMembers([(A, At(2026, 1, 1), null)]);
        var older = new DatedMembers([(A, At(2020, 1, 1), null)]);

        narrow.Hash(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)).ShouldBe(older.Hash(new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)));
    }

    private static RunHeader HeaderOf(SimulationRun run) =>
        new(run.Strategy, run.EngineVersion, run.From, run.To, run.Options, run.AcknowledgeCsvAdjusted, run.Fills, run.Universe, new XDrishti.Domain.Market.Calendar.TradingCalendar([]));

    private static UniverseBasisRecord Dated(string hash = "abc", string? parameters = null) => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), parameters, null, UniverseBasisRecord.Dated, hash);

    private static UniverseBasisRecord Snapshot() => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), null, new DateOnly(2026, 3, 4), UniverseBasisRecord.SingleSnapshot, null);

    [Fact]
    public void The_label_of_a_dated_run_is_the_one_of_the_spec()
    {
        UniverseLabels.Dated(Guid.Parse("11111111-1111-1111-1111-111111111111"), null).ShouldBe("members as of each day (dated); basket 11111111-1111-1111-1111-111111111111; params -");
        UniverseLabels.Dated(Guid.Parse("11111111-1111-1111-1111-111111111111"), "ph").ShouldEndWith("params ph");
    }

    [Fact]
    public void The_key_line_is_added_only_for_a_run_that_named_a_basis_and_changes_with_the_history()
    {
        var plain = FabricatedRun.Standard();
        var header = HeaderOf(plain);

        var none = RunAssembler.KeyText(header, plain.Instruments, plain.CalendarExceptions);
        var dated = RunAssembler.KeyText(header with { UniverseBasis = Dated("h1") }, plain.Instruments, plain.CalendarExceptions);
        var datedOther = RunAssembler.KeyText(header with { UniverseBasis = Dated("h2") }, plain.Instruments, plain.CalendarExceptions);
        var snapshot = RunAssembler.KeyText(header with { UniverseBasis = Snapshot() }, plain.Instruments, plain.CalendarExceptions);

        none.ShouldNotContain("umembers=");
        dated.ShouldContain("umembers=dated|h1|-\n");
        datedOther.ShouldNotBe(dated);
        snapshot.ShouldContain("umembers=single_snapshot|2026-03-04|-\n");
        dated.Replace("umembers=dated|h1|-\n", string.Empty, StringComparison.Ordinal).ShouldBe(none);
    }

    [Fact]
    public void The_key_never_holds_the_owners_registered_list()
    {
        // the key text is made from the header and the records only: nothing here reads a setting (ADR 0261 point 9)
        var plain = FabricatedRun.Standard();
        var header = HeaderOf(plain) with { UniverseBasis = Dated() };

        RunAssembler.KeyText(header, plain.Instruments, plain.CalendarExceptions).ShouldNotContain("registered");
    }

    [Fact]
    public void Point_in_time_is_said_only_for_a_dated_run()
    {
        var plain = FabricatedRun.Standard();
        var datedRun = plain with { UniverseBasis = Dated() };
        var snapshotRun = plain with { UniverseBasis = Snapshot() };

        string[] Lines(SimulationRun run) => [.. RunLabels.Of(run, FillRules.Default).Lines];

        Lines(plain).ShouldNotContain(l => l.Contains("point in time", StringComparison.OrdinalIgnoreCase));
        Lines(snapshotRun).ShouldNotContain(l => l.Contains("point in time", StringComparison.OrdinalIgnoreCase));
        Lines(snapshotRun).ShouldContain("members: single snapshot, survivorship applies");
        Lines(datedRun).ShouldContain("members: point in time (dated members)");
        UniverseLabels.Basket(Guid.NewGuid()).ShouldNotContain("point in time");
    }

    [Fact]
    public void The_not_member_fate_is_counted_as_a_no_trade_and_added()
    {
        var fates = RunFates.Empty with { NotMember = 2, TurnoverBlocked = 1 };

        fates.NoTrade.ShouldBe(3);
        fates.Add(fates).NotMember.ShouldBe(4);
        (WalkCounters.Empty with { NotMember = 1 }).Add(WalkCounters.Empty with { NotMember = 2 }).NotMember.ShouldBe(3);
    }
}
