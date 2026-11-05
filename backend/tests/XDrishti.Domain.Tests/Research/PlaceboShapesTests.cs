using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>CF-07 part B, criterion 4 (ADR 0209): the generator yields exactly 20 named shapes in a fixed order; none looks ahead; the mirror flips the side; an instrument roll is a permutation of the instruments.</summary>
public sealed class PlaceboShapesTests
{
    private static readonly Guid[] Ids = [.. Enumerable.Range(1, 7).Select(i => new Guid($"00000000-0000-0000-0000-{i:D12}"))];

    private static PlaceboSignal Signal(int id = 0, int instrument = 0, Side side = Side.Buy) =>
        new(id, Ids[instrument], new DateOnly(2026, 3, 2), side, 0.01, 2, CostSegment.IntradayEquity, 1_772_000_000);

    [Fact]
    public void There_are_exactly_twenty_shapes_with_the_names_and_order_of_the_spec()
    {
        PlaceboShapes.All.Select(s => s.Name).ShouldBe([
            "mirror",
            "shift_+15", "shift_+30", "shift_+45", "shift_+60",
            "date_shift_+1", "date_shift_+2", "date_shift_+3", "date_shift_+4", "date_shift_+5",
            "date_shift_-1", "date_shift_-2", "date_shift_-3", "date_shift_-4", "date_shift_-5",
            "instrument_roll_1", "instrument_roll_2", "instrument_roll_3", "instrument_roll_4", "instrument_roll_5"]);
        PlaceboShapes.All.Count.ShouldBe(PlaceboShapes.Count);
        PlaceboShapes.All.Select(s => s.Name).Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public void The_generator_is_deterministic_and_has_no_free_parameter()
    {
        var order = PlaceboShapes.RollOrder(Ids);

        var first = PlaceboShapes.All.Select(s => PlaceboShapes.Place(s, Signal(), order)).ToList();
        var second = PlaceboShapes.All.Select(s => PlaceboShapes.Place(s, Signal(), order)).ToList();

        first.ShouldBe(second);
    }

    [Theory]
    [InlineData(Side.Buy, Side.Sell)]
    [InlineData(Side.Sell, Side.Buy)]
    public void The_mirror_flips_the_side_and_keeps_the_instrument_the_session_and_the_minute(Side real, Side mirrored)
    {
        var p = PlaceboShapes.Place(PlaceboShapes.All[0], Signal(side: real), PlaceboShapes.RollOrder(Ids))!;

        (p.Side, p.InstrumentId, p.SessionOffset, p.MinuteOffset).ShouldBe((mirrored, Ids[0], 0, 0));
    }

    [Fact]
    public void No_shape_looks_ahead_a_shift_only_moves_forward_and_only_the_date_shifts_change_the_session()
    {
        var order = PlaceboShapes.RollOrder(Ids);

        foreach (var shape in PlaceboShapes.All)
        {
            var p = PlaceboShapes.Place(shape, Signal(), order)!;
            p.MinuteOffset.ShouldBeGreaterThanOrEqualTo(0, shape.Name);
            if (shape.Kind != PlaceboKind.ShiftSessions)
            {
                p.SessionOffset.ShouldBe(0, shape.Name);
            }

            if (shape.Kind != PlaceboKind.ShiftMinutes)
            {
                p.MinuteOffset.ShouldBe(0, shape.Name);
            }
        }

        PlaceboShapes.All.Where(s => s.Kind == PlaceboKind.ShiftMinutes).Select(s => PlaceboShapes.Place(s, Signal(), order)!.MinuteOffset).ShouldBe([15, 30, 45, 60]);
        PlaceboShapes.All.Where(s => s.Kind == PlaceboKind.ShiftSessions).Select(s => PlaceboShapes.Place(s, Signal(), order)!.SessionOffset).ShouldBe([1, 2, 3, 4, 5, -1, -2, -3, -4, -5]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void An_instrument_roll_is_a_permutation_of_the_instruments_of_the_days_signals(int k)
    {
        var order = PlaceboShapes.RollOrder(Ids);
        var shape = PlaceboShapes.All.Single(s => s.Name == "instrument_roll_" + k);
        var signals = Ids.Select((_, i) => Signal(i, i)).ToList();

        var rolled = signals.Select(s => PlaceboShapes.Place(shape, s, order)!.InstrumentId).ToList();

        rolled.OrderBy(x => x).ShouldBe(Ids.OrderBy(x => x), "every instrument receives exactly one signal");
        signals.Zip(rolled).ShouldAllBe(x => x.First.InstrumentId != x.Second, "no signal stays where it was");
        signals.Select(s => Array.IndexOf([.. order], PlaceboShapes.Place(shape, s, order)!.InstrumentId)).ShouldBe(signals.Select(s => (Array.IndexOf([.. order], s.InstrumentId) + k) % order.Count));
    }

    [Fact]
    public void A_roll_onto_itself_or_with_fewer_than_two_instruments_has_no_placement()
    {
        var two = PlaceboShapes.RollOrder(Ids.Take(2));
        var roll2 = PlaceboShapes.All.Single(s => s.Name == "instrument_roll_2");
        var roll1 = PlaceboShapes.All.Single(s => s.Name == "instrument_roll_1");

        PlaceboShapes.Place(roll2, Signal(), two).ShouldBeNull("two places on is the same instrument");
        PlaceboShapes.Place(roll1, Signal(), two).ShouldNotBeNull();
        PlaceboShapes.Place(roll1, Signal(), PlaceboShapes.RollOrder(Ids.Take(1))).ShouldBeNull();
        PlaceboShapes.Place(roll1, Signal(instrument: 3), two).ShouldBeNull("an instrument that is not in the order");
    }

    [Fact]
    public void The_roll_order_is_ascending_by_id_each_once_whatever_the_input_order()
    {
        PlaceboShapes.RollOrder([Ids[3], Ids[0], Ids[3], Ids[1]]).ShouldBe([Ids[0], Ids[1], Ids[3]]);
    }
}
