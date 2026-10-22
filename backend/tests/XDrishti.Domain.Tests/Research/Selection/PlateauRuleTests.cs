using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research.Selection;

/// <summary>
/// Criterion 3 of P4-06: the objective and the plateau rule equal a table worked out by hand. A candidate is given by its mean and its standard error of the mean (the sample is two values, mean -/+ se, so the error is exact), lambda is 0 unless
/// the case says so, and the minimum of trades is 2. The rr line is 1.5, 2, 2.5, 3; note that the id of rr 2.5 sorts before the id of rr 2 (the character '.' sorts before '}'), which decides ties.
/// </summary>
public sealed class PlateauRuleTests
{
    private const string R15 = "FIXED_RR{rr=1.5}";
    private const string R2 = "FIXED_RR{rr=2}";
    private const string R25 = "FIXED_RR{rr=2.5}";
    private const string R3 = "FIXED_RR{rr=3}";

    private sealed record Spec(ExitCandidate Exit, double Mean, double Se, int Trades = 2);

    private sealed record Case(string Name, Spec[] Candidates, string? Winner, bool Plateau = false, bool Isolated = false, bool Lone = false, bool PreferPlateau = true);

    private static Spec[] Line(params double[] means) => [.. means.Select((m, i) => new Spec(SelectionSeries.Rr(1.5 + (0.5 * i)), m, 1))];

    private static Spec Grid(double a, double b, double mean) => new(SelectionSeries.Policy("G", ("a", a), ("b", b)), mean, 1);

    private static readonly Case[] Cases =
    [
        new("a plateau in the middle wins", Line(1, 2.2, 3, 2.2), R25, Plateau: true),
        new("a sharp peak far above the plateau wins and is flagged a lone peak", Line(-1, 1, 1.1, 5), R3, Lone: true),
        new("prefer_plateau false takes the highest score and sets no flag", Line(-1, 1, 1.1, 5), R3, PreferPlateau: false),
        new("a flat line is a plateau everywhere; the highest wins", Line(3, 3.5, 3.4, 3.45), R2, Plateau: true),
        new("a peak with a lower neighbour yields to a plateau candidate within one error of it", Line(4.5, 5, 3.9, 4.4), R15, Plateau: true),
        new("equal scores tie by the lower id", Line(2, 2, 2, 2), R15, Plateau: true),
        new("equal peaks tie by the lower id (rr 2.5 sorts before rr 2)", Line(1, 3, 3, 1), R25, Lone: true),
        new("an isolated candidate far above a line is a lone peak and isolated", [.. Line(1, 1.2, 1.1, 1), new Spec(SelectionSeries.Policy("PYRAMID", ("add_at_rr", 1)), 3, 1)], "PYRAMID{add_at_rr=1}", Isolated: true, Lone: true),
        new("an isolated candidate close to a line plateau yields to the plateau", [.. Line(3, 3.2, 3.1, 3), new Spec(SelectionSeries.Policy("PYRAMID", ("add_at_rr", 1)), 3.5, 1)], R2, Plateau: true),
        new("two single candidates of different policies are not neighbours", [new Spec(SelectionSeries.Rr(2), 1, 1), new Spec(SelectionSeries.Policy("PARTIAL_BE", ("t2_rr", 3)), 2, 1)], "PARTIAL_BE{t2_rr=3}", Isolated: true, Lone: true),
        new("a neighbour with fewer than the minimum of trades is ignored", [new Spec(SelectionSeries.Rr(1.5), 2.5, 1), new Spec(SelectionSeries.Rr(2), 3, 1), new Spec(SelectionSeries.Rr(2.5), -9, 1, Trades: 1)], R2, Plateau: true),
        new("a candidate whose only neighbour is unqualified is isolated", [new Spec(SelectionSeries.Rr(2), 3, 1), new Spec(SelectionSeries.Rr(2.5), 0, 1, Trades: 1), new Spec(SelectionSeries.Policy("PARTIAL_BE", ("t2_rr", 3)), 1, 1)], R2, Isolated: true, Lone: true),
        new("one qualified candidate is no choice", [new Spec(SelectionSeries.Rr(2), 3, 1), new Spec(SelectionSeries.Rr(2.5), 4, 1, Trades: 1)], null),
        new("no qualified candidate is no choice", [new Spec(SelectionSeries.Rr(2), 3, 1, Trades: 1), new Spec(SelectionSeries.Rr(2.5), 4, 1, Trades: 0)], null),
        new("a grid peak whose axis neighbours are lower is a lone peak", [Grid(1, 1, 1), Grid(1, 2, 2), Grid(2, 1, 2), Grid(2, 2, 5)], "G{a=2,b=2}", Lone: true),
        new("a grid plateau", [Grid(1, 1, 4), Grid(1, 2, 4.2), Grid(2, 1, 4.1), Grid(2, 2, 4.3)], "G{a=2,b=2}", Plateau: true),
        new("grid diagonals are not neighbours", [Grid(1, 1, 3), Grid(1, 2, 0), Grid(2, 1, 0), Grid(2, 2, 3.2)], "G{a=2,b=2}", Lone: true),
        new("a three-by-one line plus a second line of another policy are two lines", [.. Line(1, 1.5, 1.2), new Spec(SelectionSeries.Policy("PARTIAL_BE", ("t2_rr", 3)), 1.4, 1), new Spec(SelectionSeries.Policy("PARTIAL_BE", ("t2_rr", 4)), 1.3, 1)], R2, Plateau: true),
        new("the error of the candidate itself sets the tolerance", [new Spec(SelectionSeries.Rr(1.5), 1, 1), new Spec(SelectionSeries.Rr(2), 3, 3)], R2, Plateau: true),
        new("a candidate with a small error does not tolerate a much lower neighbour", [new Spec(SelectionSeries.Rr(1.5), 1, 1), new Spec(SelectionSeries.Rr(2), 3, 0.5)], R2, Lone: true),
        new("the lowest of a line is a plateau but not within one error of the best", Line(0, 0.5, 4, 0.5), R25, Lone: true),
        new("an all-negative field still picks the least bad", Line(-3, -1, -2, -4), R2, Lone: true),
    ];

    [Fact]
    public void The_winner_and_the_flags_equal_the_hand_table_on_every_case()
    {
        Cases.Length.ShouldBeGreaterThanOrEqualTo(20);
        foreach (var c in Cases)
        {
            var options = new SelectionOptions(MinTrades: 2, Lambda: 0, PreferPlateau: c.PreferPlateau);

            var choice = ExitSelector.Choose([.. c.Candidates.Select(s => s.Exit)], [.. c.Candidates.Select(Sample)], options);

            choice?.Id.ShouldBe(c.Winner, c.Name);
            if (c.Winner is not null)
            {
                choice!.OnPlateau.ShouldBe(c.Plateau, c.Name + ": plateau");
                choice.Isolated.ShouldBe(c.Isolated, c.Name + ": isolated");
                choice.LonePeak.ShouldBe(c.Lone, c.Name + ": lone peak");
            }
        }
    }

    [Fact]
    public void The_order_in_which_the_candidates_are_given_changes_no_choice()
    {
        foreach (var c in Cases)
        {
            var options = new SelectionOptions(MinTrades: 2, Lambda: 0, PreferPlateau: c.PreferPlateau);
            var forward = ExitSelector.Choose([.. c.Candidates.Select(s => s.Exit)], [.. c.Candidates.Select(Sample)], options);
            var reversed = c.Candidates.Reverse().ToArray();

            var back = ExitSelector.Choose([.. reversed.Select(s => s.Exit)], [.. reversed.Select(Sample)], options);

            back.ShouldBe(forward, c.Name);
        }
    }

    [Fact]
    public void Multiplying_every_R_by_a_positive_constant_changes_no_choice()
    {
        foreach (var c in Cases)
        {
            var options = new SelectionOptions(MinTrades: 2, Lambda: 0.5, PreferPlateau: c.PreferPlateau);
            var plain = ExitSelector.Choose([.. c.Candidates.Select(s => s.Exit)], [.. c.Candidates.Select(Sample)], options);

            var scaled = ExitSelector.Choose([.. c.Candidates.Select(s => s.Exit)], [.. c.Candidates.Select(s => (IReadOnlyList<double>)[.. Sample(s).Select(v => v * 1024)])], options);

            scaled.ShouldBe(plain, c.Name);
        }
    }

    [Fact]
    public void The_downside_deviation_and_the_score_equal_the_hand_value()
    {
        // R = 2, -1, -3, 0: mean -0.5, squared losses 1 and 9 over 4 trades, dd = sqrt(2.5) = 1.58113883...
        double[] r = [2, -1, -3, 0];

        ExitSelector.Score(r, 0.5)!.Value.ShouldBe(-0.5 - (0.5 * Math.Sqrt(2.5)), 1e-12);
        ExitSelector.Score(r, 0)!.Value.ShouldBe(-0.5, 1e-12);
        ExitSelector.Score([0.5, 1.5], 0.5)!.Value.ShouldBe(1.0, 1e-12, "no losses, no downside");
        ExitSelector.Score([], 0.5).ShouldBeNull();
    }

    [Fact]
    public void The_downside_deviation_counts_the_wins_in_the_denominator()
    {
        // R = -2, 1, 1, 1: mean 0.25, dd = sqrt(4 / 4) = 1, score at lambda 0.5 = -0.25
        ExitSelector.Score([-2, 1, 1, 1], 0.5)!.Value.ShouldBe(-0.25, 1e-12);
    }

    private static IReadOnlyList<double> Sample(Spec s) => s.Trades switch
    {
        0 => [],
        1 => [s.Mean],
        _ => [s.Mean - s.Se, s.Mean + s.Se],
    };
}
