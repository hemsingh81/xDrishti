using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criteria 2 and 4 of P3-03a: the scanner produces exactly the hand-made candidates of the tables (the scanner column of P3-03's days and of D12), field for field, and none where the tables say none.</summary>
public sealed class CandidateTests
{
    private static readonly string CandB = Expect.CandB();

    private static readonly string Sell29 = Expect.Candidate(Side.Sell, 29, "09:45", 100.00, "09:30", 99.00, 99.00, 97.50, 97.50, 15, true);

    /// <summary>The candidates (both sides, BUY first) of the ORB day, as text without the trace.</summary>
    private static string[] Candidates(Fixture fixture) =>
        [.. Scan.Both(fixture.Definition, fixture.Bars, fixture.Timeframe).SelectMany(s => s.Candidates).Select(c => Render.Candidate(c, withTrace: false))];

    public static TheoryData<string, string[]> Expected => new()
    {
        { "F1", [CandB] },
        { "F2", [CandB] },
        { "F3", [CandB] },
        { "F4", [CandB] },
        { "F5", [CandB] },
        { "F6", [CandB] },
        { "F7", [CandB] },
        { "F8", [] },
        { "F9", [Expect.Candidate(Side.Buy, 134, "11:30", 98.00, "11:15", 99.00, 100.50, 99.00, 100.50, 15, true)] },
        { "F10", [] },
        { "F11", [CandB] },
        { "F12", [Sell29] },
        { "F13", [CandB, Expect.Candidate(Side.Sell, 59, "10:15", 100.00, "10:00", 100.50, 100.50, 97.50, 97.50, 15, true)] },
        { "F14", [CandB] },
        { "F15", [Expect.Candidate(Side.Buy, 29, "09:46", 98.00, "09:30", 99.00, 100.50, 99.00, 100.50, 14, false)] },
        { "F16", [Expect.Candidate(Side.Buy, 14, "09:45", 98.00, "09:30", 99.00, 100.50, 99.00, 100.50, 1, false)] },
        { "F17", [CandB] },
        { "F17b-first", [CandB] },
        { "F17b-second", [CandB] },
        { "F18", [] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void The_scanner_makes_exactly_the_hand_made_candidates(string name, string[] expected)
    {
        var fixture = Fixtures.OrbDays.Single(f => f.Name == name);

        Candidates(fixture).ShouldBe(expected);
    }

    [Fact]
    public void Both_sides_have_a_candidate_on_one_date_in_f13()
    {
        var scanners = Scan.Both(OrbV1.Create(), Fixtures.F13().Bars());

        scanners[0].Candidates.Select(c => c.Side).ShouldBe([Side.Buy]);
        scanners[1].Candidates.Select(c => c.Side).ShouldBe([Side.Sell]);
        scanners[0].Candidates[0].Instrument.ShouldBe(Scan.Instrument);
    }
}
