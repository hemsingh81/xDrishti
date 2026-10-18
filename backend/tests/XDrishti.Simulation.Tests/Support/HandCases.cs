using XDrishti.Domain.Research;
using static XDrishti.Domain.Research.EntryFill;
using static XDrishti.Domain.Research.ExitReason;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>What the table says a hand-calculated trade gives: exact and filled prices, R, MAE and MFE (rounded to 4 places), and the counters that are not zero.</summary>
internal sealed record Want(
    decimal Entry, decimal EntryFilled, decimal Exit, decimal ExitFilled, ExitReason Reason, EntryFill Kind, decimal RExact, decimal RFilled, decimal SlippageR, decimal Mae, decimal Mfe, Fx Fx = default);

/// <summary>The counters of a trade other than the entry kind: each is 0 or false unless the table names it.</summary>
internal readonly record struct Fx(bool StopGap = false, bool TargetGap = false, bool Ambiguous = false, bool Late = false, bool Under4 = false, bool Missing = false, int Zero = 0);

internal sealed record HandCase(Hand Hand, Want Want)
{
    public override string ToString() => Hand.Name;
}

/// <summary>The trades H1 to H25 of P3-03b (<i>Hand-calculated cases</i>), every number worked out by hand in the spec; the doc 16 slippage defaults, tick 0.05 unless a row says otherwise.</summary>
internal static class HandCases
{
    /// <summary>Fill minute A: <c>AtOpen</c>, entry 100.00, risk 1.00, target 102.00; modelled entry 100.10 (the limit).</summary>
    private const string A = "10:00 100.00/100.30/99.80/100.20";

    /// <summary>Fill minute of ticket S (the sell of H12): <c>AtOpen</c> 199.90, risk 2.10, target 195.70; modelled entry 199.80 (the limit).</summary>
    private const string S = "10:00 199.90/200.20/199.50/199.60";

    public static IReadOnlyList<HandCase> All { get; } = Build();

    public static HandCase Get(string name) => All.Single(c => c.Hand.Name == name);

    private static Hand Ticket(string name) => Hand.Sell(name, 200.00, 202.00);

    private static List<HandCase> Build() =>
    [
        new(Hand.Buy("H1").Rows(A, "10:40 101.50/102.10/101.40/102.00"),
            new(100.00m, 100.10m, 102.00m, 102.00m, Target, AtOpen, 2.0000m, 1.9000m, -0.1000m, 0.2000m, 2.0000m)),
        new(Hand.Buy("H2").Rows("10:00 100.30/100.50/100.00/100.40", "15:15 101.00/101.20/100.90/101.10"),
            new(100.10m, 100.10m, 101.00m, 100.90m, SquareOff, AtLimit, 0.8182m, 0.7273m, -0.0909m, 0.0909m, 0.8182m)),
        new(Hand.Buy("H3").Rows(A, "10:08 98.60/98.90/98.40/98.50"),
            new(100.00m, 100.10m, 98.60m, 98.40m, Stop, AtOpen, -1.4000m, -1.7000m, -0.3000m, 1.4000m, 0.3000m, new(StopGap: true))),
        new(Hand.Buy("H4").Rows(A, "10:20 99.60/99.70/98.90/99.00"),
            new(100.00m, 100.10m, 99.00m, 98.80m, Stop, AtOpen, -1.0000m, -1.3000m, -0.3000m, 1.0000m, 0.3000m)),
        new(Hand.Buy("H5").Rows(A, "10:30 103.20/103.50/103.00/103.40"),
            new(100.00m, 100.10m, 102.00m, 102.00m, Target, AtOpen, 2.0000m, 1.9000m, -0.1000m, 0.2000m, 2.0000m, new(TargetGap: true))),
        new(Hand.Buy("H6").Rows(A, "10:15 100.50/102.40/98.70/100.00"),
            new(100.00m, 100.10m, 99.00m, 98.65m, Stop, AtOpen, -1.0000m, -1.4500m, -0.4500m, 1.0000m, 0.3000m, new(Ambiguous: true))),
        new(Hand.Buy("H7").Rows(A, "10:25 103.00/103.30/98.90/100.00"),
            new(100.00m, 100.10m, 102.00m, 102.00m, Target, AtOpen, 2.0000m, 1.9000m, -0.1000m, 0.2000m, 2.0000m, new(TargetGap: true))),
        new(Hand.Buy("H8").Rows("10:00 100.00/100.20/98.90/99.50"),
            new(100.00m, 100.10m, 99.00m, 98.75m, Stop, AtOpen, -1.0000m, -1.3500m, -0.3500m, 1.0000m, 0.0000m)),
        new(Hand.Buy("H9").Rows(A, "10:01 100.10/100.20/99.90/100.00", "10:07 98.20/98.50/98.00/98.30").Absent("10:02", "10:06"),
            new(100.00m, 100.10m, 98.20m, 98.00m, Stop, AtOpen, -1.8000m, -2.1000m, -0.3000m, 1.8000m, 0.3000m, new(StopGap: true))),
        new(Hand.Buy("H10").Rows(A, "15:17 100.30/100.45/100.20/100.25").Absent("15:15", "15:16"),
            new(100.00m, 100.10m, 100.30m, 100.20m, SquareOff, AtOpen, 0.3000m, 0.1000m, -0.2000m, 0.2000m, 0.3000m, new(Late: true))),
        new(Hand.Buy("H11").Rows(A, "15:15 98.90/99.00/98.80/98.85"),
            new(100.00m, 100.10m, 98.90m, 98.70m, Stop, AtOpen, -1.1000m, -1.4000m, -0.3000m, 1.1000m, 0.3000m, new(StopGap: true))),
        new(Ticket("H12").Rows(S, "11:00 203.10/203.60/202.90/203.20"),
            new(199.90m, 199.80m, 203.10m, 203.30m, Stop, AtOpen, -1.5238m, -1.6667m, -0.1429m, 1.5238m, 0.1905m, new(StopGap: true))),
        new(Ticket("H13").Rows("10:00 199.40/199.90/199.20/199.30", "12:00 196.00/196.30/195.30/195.60"),
            new(199.80m, 199.80m, 195.40m, 195.40m, Target, AtLimit, 2.0000m, 2.0000m, 0.0000m, 0.0455m, 2.0000m)),
        new(Hand.Buy("H14", 123.47, 122.42).Rows("10:00 123.42/123.60/123.30/123.50", "15:15 124.47/124.60/124.30/124.50"),
            new(123.42m, 123.50m, 124.47m, 124.40m, SquareOff, AtOpen, 1.0500m, 0.9000m, -0.1500m, 0.1200m, 1.0500m)),
        new(Hand.Buy("H15").Rows("10:00 100.05/100.30/99.90/100.20", "10:50 102.00/102.30/101.90/102.20"),
            new(100.05m, 100.10m, 102.15m, 102.15m, Target, AtOpen, 2.0000m, 1.9524m, -0.0476m, 0.1429m, 2.0000m)),
        new(Hand.Buy("H16").Rows(A, "14:50 100.50/100.60/100.40/100.55").EndsAfter("14:50"),
            new(100.00m, 100.10m, 100.55m, 100.45m, EndOfData, AtOpen, 0.5500m, 0.3500m, -0.2000m, 0.2000m, 0.6000m)),
        new(Ticket("H17").Rows(S, "10:30 195.20/195.40/194.90/195.00"),
            new(199.90m, 199.80m, 195.70m, 195.70m, Target, AtOpen, 2.0000m, 1.9524m, -0.0476m, 0.1429m, 2.0000m, new(TargetGap: true))),
        new(Ticket("H18").Rows(S, "10:25 195.50/202.40/195.40/199.00"),
            new(199.90m, 199.80m, 195.70m, 195.70m, Target, AtOpen, 2.0000m, 1.9524m, -0.0476m, 0.1429m, 2.0000m, new(TargetGap: true))),
        new(Ticket("H19").Rows(S, "15:15 202.30/202.60/202.10/202.20"),
            new(199.90m, 199.80m, 202.30m, 202.50m, Stop, AtOpen, -1.1429m, -1.2857m, -0.1429m, 1.1429m, 0.1905m, new(StopGap: true))),
        new(Ticket("H20").Rows(S, "10:15 199.50/202.40/195.00/199.00"),
            new(199.90m, 199.80m, 202.00m, 202.55m, Stop, AtOpen, -1.0000m, -1.3095m, -0.3095m, 1.0000m, 0.1905m, new(Ambiguous: true))),
        new(Hand.Buy("H21", 100.00, 98.85).Rows(A, "10:40 101.50/102.30/101.40/102.00"),
            new(100.00m, 100.10m, 102.30m, 102.30m, Target, AtOpen, 2.0000m, 1.9130m, -0.0870m, 0.1739m, 2.0000m)),
        new(Hand.Sell("H22", 200.00, 200.90).Rows("10:00 199.85/200.10/199.60/199.70", "10:30 198.50/198.60/197.75/198.00"),
            new(199.85m, 199.80m, 197.75m, 197.75m, Target, AtOpen, 2.0000m, 1.9524m, -0.0476m, 0.2381m, 2.0000m)),
        new(Hand.Buy("H23", 100.00, 99.05).Rows(A, "10:20 99.60/99.70/99.05/99.30"),
            new(100.00m, 100.10m, 99.05m, 98.85m, Stop, AtOpen, -1.0000m, -1.3158m, -0.3158m, 1.0000m, 0.3158m)),
        new(Hand.Buy("H24").Rows(
                "10:00 100.60/100.60/100.60/100.60 v0", "10:01 100.00/100.20/99.90/100.10", "10:20 100.20/100.30/98.50/100.20 v0", "15:15 98.90/99.00/98.80/100.20 v0", "15:16 100.30/100.45/100.20/100.25"),
            new(100.00m, 100.10m, 100.30m, 100.20m, SquareOff, AtOpen, 0.3000m, 0.1000m, -0.2000m, 0.1000m, 0.3000m, new(Late: true, Missing: true, Zero: 3))),
        new(Hand.Buy("H25", 20.00, 19.70).WithTick(0.10m).Rows("10:00 20.00/20.10/19.90/20.00", "10:20 19.80/19.85/19.60/19.70"),
            new(20.00m, 20.00m, 19.70m, 19.30m, Stop, AtOpen, -1.0000m, -2.3333m, -1.3333m, 1.0000m, 0.3333m, new(Under4: true))),
    ];
}
