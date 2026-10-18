using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criterion 7 of P3-04b1: the net for the unit of the stored tick (paise by ADR 0038 item 6).</summary>
public sealed class TickUnitCheckTests
{
    private static TickProbe Probe(string symbol, InstrumentType type, decimal? stored) => new(Guid.NewGuid(), symbol, type, stored);

    [Fact]
    public void Only_a_share_stored_in_rupees_is_a_suspect()
    {
        var rupees = Probe("NSE:RUPEES", InstrumentType.Share, 0.05m);
        TickProbe[] probes =
        [
            Probe("NSE:ONE", InstrumentType.Share, 1m),
            Probe("NSE:FIVE", InstrumentType.Share, 5m),
            Probe("NSE:TEN", InstrumentType.Etf, 10m),
            rupees,
            Probe("NSE:NIFTY", InstrumentType.Index, 0.05m),
            Probe("NSE:FUT", InstrumentType.Future, 0.25m),
            Probe("NSE:NONE", InstrumentType.Share, null),
            Probe("NSE:HIGH", InstrumentType.Share, 150m),
        ];

        var suspects = TickUnitCheck.Suspect(probes);

        suspects.Count.ShouldBe(1);
        suspects[0].Probe.ShouldBe(rupees);
        suspects[0].Probe.StoredTick.ShouldBe(0.05m);
        suspects[0].ConvertedRupees.ShouldBe(0.0005m);
    }

    [Theory]
    [InlineData(InstrumentType.Share)]
    [InlineData(InstrumentType.Etf)]
    public void A_share_or_etf_stored_as_a_half_is_a_suspect_too(InstrumentType type)
    {
        var suspects = TickUnitCheck.Suspect([Probe("NSE:HALF", type, 0.5m)]);

        suspects.Single().ConvertedRupees.ShouldBe(0.005m);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0.99, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void The_edge_is_one_paisa(double stored, bool suspect)
    {
        TickUnitCheck.Suspect([Probe("NSE:EDGE", InstrumentType.Share, (decimal)stored)]).Any().ShouldBe(suspect);
    }

    [Fact]
    public void An_index_and_a_future_are_never_suspects_whatever_the_tick()
    {
        TickUnitCheck.Suspect([Probe("A", InstrumentType.Index, 0.0001m), Probe("B", InstrumentType.Future, 0.0001m)]).ShouldBeEmpty();
    }

    [Fact]
    public void The_suspects_come_in_the_order_given()
    {
        var first = Probe("NSE:A", InstrumentType.Share, 0.05m);
        var second = Probe("NSE:B", InstrumentType.Etf, 0.01m);

        TickUnitCheck.Suspect([first, Probe("NSE:OK", InstrumentType.Share, 5m), second]).Select(s => s.Probe).ShouldBe([first, second]);
    }

    [Fact]
    public void A_null_list_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => TickUnitCheck.Suspect(null!));
    }
}
