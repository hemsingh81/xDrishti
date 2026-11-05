using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// The placebo generator of CF-07 (D2): 20 fixed, named shapes over the real signals, in a fixed order, with no free parameter. <c>mirror</c> (1), <c>shift_+15/+30/+45/+60</c> (4), <c>date_shift_+1..+5</c> then <c>date_shift_-1..-5</c> (10) and
/// <c>instrument_roll_1..5</c> (5). Pure and deterministic: it decides only where a signal goes; the walk and the costs are the engine's. No shape reads earlier minutes of the same session than the real decision.
/// </summary>
public static class PlaceboShapes
{
    /// <summary>The number of shapes.</summary>
    public const int Count = 20;

    private static readonly int[] ShiftMinutes = [15, 30, 45, 60];

    /// <summary>The shapes, in their fixed order.</summary>
    public static IReadOnlyList<PlaceboShape> All { get; } = Build();

    /// <summary>The fixed order instruments are rolled in: ascending by id, each once.</summary>
    public static IReadOnlyList<Guid> RollOrder(IEnumerable<Guid> instruments)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        return [.. instruments.Distinct().Order()];
    }

    /// <summary>
    /// Where <paramref name="shape"/> puts <paramref name="signal"/>, or null when it cannot (an instrument roll with fewer than two instruments, or onto itself). <paramref name="rollOrder"/> is <see cref="RollOrder"/>. The roll is a cyclic shift of that order, so for any
    /// roll the instruments of one day's signals are mapped one to one.
    /// </summary>
    public static PlaceboPlacement? Place(PlaceboShape shape, PlaceboSignal signal, IReadOnlyList<Guid> rollOrder)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(rollOrder);
        switch (shape.Kind)
        {
            case PlaceboKind.Mirror:
                return new PlaceboPlacement(signal.InstrumentId, 0, 0, signal.Side == Side.Buy ? Side.Sell : Side.Buy);
            case PlaceboKind.ShiftMinutes:
                return new PlaceboPlacement(signal.InstrumentId, 0, shape.Amount, signal.Side);
            case PlaceboKind.ShiftSessions:
                return new PlaceboPlacement(signal.InstrumentId, shape.Amount, 0, signal.Side);
            case PlaceboKind.RollInstrument:
                var at = IndexOf(rollOrder, signal.InstrumentId);
                if (at < 0 || rollOrder.Count < 2)
                {
                    return null;
                }

                var to = (at + shape.Amount) % rollOrder.Count;
                return to == at ? null : new PlaceboPlacement(rollOrder[to], 0, 0, signal.Side);
            default:
                return null;
        }
    }

    private static int IndexOf(IReadOnlyList<Guid> order, Guid id)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Signed(int value) => value.ToString("+0;-0", CultureInfo.InvariantCulture);

    private static List<PlaceboShape> Build()
    {
        var shapes = new List<PlaceboShape> { new("mirror", PlaceboKind.Mirror, 0) };
        shapes.AddRange(ShiftMinutes.Select(m => new PlaceboShape("shift_" + Signed(m), PlaceboKind.ShiftMinutes, m)));
        shapes.AddRange(Enumerable.Range(1, 5).Select(k => new PlaceboShape("date_shift_" + Signed(k), PlaceboKind.ShiftSessions, k)));
        shapes.AddRange(Enumerable.Range(1, 5).Select(k => new PlaceboShape("date_shift_" + Signed(-k), PlaceboKind.ShiftSessions, -k)));
        shapes.AddRange(Enumerable.Range(1, 5).Select(k => new PlaceboShape("instrument_roll_" + k.ToString(CultureInfo.InvariantCulture), PlaceboKind.RollInstrument, k)));
        return shapes;
    }
}
