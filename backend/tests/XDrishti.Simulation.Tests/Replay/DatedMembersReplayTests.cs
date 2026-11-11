using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// BK-11b criterion 2 through the replay: a dated run takes an entry only on a day the instrument was a member; a constant membership gives the record of a run that was not asked for dated members; a leaver keeps no new entries
/// and the identity of the fates holds with the new fate.
/// </summary>
public sealed class DatedMembersReplayTests
{
    private static readonly PreparedStrategy Plain = Rep.Prepare(TestStrategies.OrbBuyOnly());

    private static InstrumentReplay Replay(Func<DateOnly, bool>? memberOn) =>
        InstrumentReplayer.Replay(Plain, Rep.Input(Cases.F("F1").Bars, Mon, Mon) with { MemberOn = memberOn });

    [Fact]
    public void A_member_on_the_entry_date_gives_the_record_of_a_run_without_a_membership_test()
    {
        var plain = Replay(null);
        var dated = Replay(_ => true);

        RunText.Replay(dated).ShouldBe(RunText.Replay(plain));
        plain.Trades.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_signal_on_a_day_the_instrument_is_not_a_member_is_not_taken_and_is_counted()
    {
        var plain = Replay(null);
        var replay = Replay(day => day != Mon);

        ReplayIdentities.ShouldHold(replay);
        replay.Trades.ShouldBeEmpty();
        replay.Record.Fates.NotMember.ShouldBe(plain.Trades.Count);
        replay.Record.Walk.NotMember.ShouldBe(plain.Trades.Count);
        replay.Record.Fates.Trades.ShouldBe(0);
    }

    [Fact]
    public void Every_no_trade_reason_has_a_fate_including_the_new_one()
    {
        InstrumentReplayer.NoTradeFate(NoTradeReason.NotMember, "TEST").NotMember.ShouldBe(1);
        InstrumentReplayer.NoTradeFate(NoTradeReason.NotMember, "TEST").NoTrade.ShouldBe(1);
    }
}
