using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using static XDrishti.Domain.Research.ExitReason;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>What the table of P3-03d says a hand-calculated swing trade gives: the exact and filled exit, R, MAE and MFE (4 places), the sessions and the counters that are not zero.</summary>
internal sealed record SwingWant(
    decimal Exit, decimal ExitFilled, ExitReason Reason, decimal RExact, decimal RFilled, decimal SlippageR, decimal Mae, decimal Mfe, int Held, int Unseen, int Examined, string Exits,
    bool StopGap = false, bool TargetGap = false, bool OvernightStopGap = false, bool OvernightTargetGap = false, bool MaxHoldAtLastClose = false);

internal sealed record SwingCase(SwingHand Hand, SwingWant Want)
{
    public override string ToString() => Hand.Name;
}

/// <summary>The rows M1 to M7b of P3-03d (<i>Hand-calculated cases</i>), every number worked out by hand in the spec. Setup A, the doc 16 slippage, the default week unless a row says otherwise.</summary>
internal static class SwingCases
{
    public static IReadOnlyList<SwingCase> All { get; } = Build();

    public static SwingCase Get(string name) => All.Single(c => c.Hand.Name == name);

    private static SwingHand A(string name, int sessions, TradingCalendar? calendar = null) => SwingHand.A(name, sessions, calendar);

    /// <summary>M3 and the rows built on it: Tuesday <c>10:30 100.20/101.20/100.10/101.00</c> then flat 101.00.</summary>
    private static SwingHand M3Tuesday(string name, int sessions) => A(name, sessions).Then(Tue, "10:30 100.20/101.20/100.10/101.00");

    private static List<SwingCase> Build() =>
    [
        new(A("M1", 5).Then(Tue, "09:15 98.60/98.90/98.40/98.50"), new(98.60m, 98.40m, Stop, -1.4000m, -1.7000m, -0.3000m, 1.4000m, 0.3000m, 2, 0, 331, "stop", StopGap: true, OvernightStopGap: true)),
        new(A("M1b", 5).Then(Tue, "09:15 98.62/98.90/98.40/98.50"), new(98.62m, 98.40m, Stop, -1.3800m, -1.7000m, -0.3200m, 1.3800m, 0.3000m, 2, 0, 331, "stop", StopGap: true, OvernightStopGap: true)),
        new(A("M2", 5).Then(Tue, "11:00 100.00/100.00/99.10/99.40").Then(Wed, "10:40 99.60/102.10/99.50/102.00"), new(102.00m, 102.00m, Target, 2.0000m, 1.9000m, -0.1000m, 0.9000m, 2.0000m, 3, 0, 791, "target")),
        new(M3Tuesday("M3", 3).Then(Wed, "15:29 100.60/100.80/100.50/100.70"), new(100.70m, 100.60m, MaxHold, 0.7000m, 0.5000m, -0.2000m, 0.2000m, 1.2000m, 3, 0, 1080, "maxhold")),
        new(M3Tuesday("M3b", 2).Then(Wed, "15:29 100.60/100.80/100.50/100.70"), new(101.00m, 100.95m, MaxHold, 1.0000m, 0.8500m, -0.1500m, 0.2000m, 1.2000m, 2, 0, 705, "maxhold")),
        new(M3Tuesday("M3c", 1).Then(Wed, "15:29 100.60/100.80/100.50/100.70"), new(100.20m, 100.15m, MaxHold, 0.2000m, 0.0500m, -0.1500m, 0.2000m, 0.3000m, 1, 0, 330, "maxhold")),
        new(A("M4a", 3, HolidayOnTuesday()).Then(Wed, "15:29 100.20/100.40/100.10/100.30").Then(Thu, "15:29 100.30/100.50/100.20/100.40"), new(100.40m, 100.30m, MaxHold, 0.4000m, 0.2000m, -0.2000m, 0.2000m, 0.5000m, 3, 0, 1080, "maxhold")),
        new(A("M4b", 3).Then(Wed, "15:29 100.20/100.40/100.10/100.30").Then(Thu, "15:29 100.30/100.50/100.20/100.40"), new(100.30m, 100.20m, MaxHold, 0.3000m, 0.1000m, -0.2000m, 0.2000m, 0.4000m, 3, 1, 705, "maxhold")),
        new(A("M5", 5).Flat(Tue), new(100.20m, 100.15m, EndOfData, 0.2000m, 0.0500m, -0.1500m, 0.2000m, 0.3000m, 2, 0, 705, "endofdata")),
        new(A("M6", 3).Flat(Tue).Then(Wed, "15:29 100.60/100.80/98.90/99.20"), new(99.00m, 98.75m, Stop, -1.0000m, -1.3500m, -0.3500m, 1.0000m, 0.3000m, 3, 0, 1080, "stop")),
        new(M7("M7a", withThursday: true), new(100.50m, 100.40m, MaxHold, 0.5000m, 0.3000m, -0.2000m, 0.2000m, 1.2000m, 3, 0, 1079, "maxhold", MaxHoldAtLastClose: true)),
        new(M7("M7b", withThursday: false), new(100.50m, 100.40m, EndOfData, 0.5000m, 0.3000m, -0.2000m, 0.2000m, 1.2000m, 3, 0, 1079, "endofdata")),
    ];

    /// <summary>M7a and M7b: M3 with Wednesday 15:28 listed and the 15:29 minute at Volume 0; a Thursday session follows in M7a and the list ends at Wednesday 15:29 in M7b.</summary>
    private static SwingHand M7(string name, bool withThursday)
    {
        var hand = M3Tuesday(name, 3).ThenWith(Wed, null, ["15:29 100.60/100.80/100.50/100.70"], "15:28 100.50/100.55/100.45/100.50");
        return withThursday ? hand.Flat(Thu) : hand;
    }
}
