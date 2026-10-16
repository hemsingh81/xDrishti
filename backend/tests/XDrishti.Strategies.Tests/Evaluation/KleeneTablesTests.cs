using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// Criterion 8, first half: the three-valued tables of <c>and</c>, <c>or</c>, <c>not</c> and of <c>==</c>/<c>!=</c> between truth values, pinned exhaustively <i>through the compiled conditions</i> (the functions
/// behind them are walked through in <see cref="KernelTests"/>). Three atoms of known value on a frame of two bars: <c>close &gt; 5</c> is true, <c>close &lt; 5</c> is false and <c>sma(5) &gt; 0</c> is unknown (an average of
/// five bars cannot be ready after two). The tables are written out by hand; the decision (<c>Evaluate</c>) is true exactly where the truth is true.
/// </summary>
public sealed class KleeneTablesTests
{
    private static readonly string[] Atoms = ["close < 5", "sma(5) > 0", "close > 5"];

    private static readonly Truth[] Values = [Truth.False, Truth.Unknown, Truth.True];

    private static readonly string[] AndTable = ["FFF", "FUU", "FUT"];

    private static readonly string[] OrTable = ["FUT", "UUT", "TTT"];

    private static readonly string[] EqualTable = ["TUF", "UUU", "FUT"];

    private static readonly string[] NotEqualTable = ["FUT", "UUU", "TUF"];

    private static readonly string[] NotTable = ["T", "U", "F"];

    private static Truth Of(char letter) => letter switch { 'T' => Truth.True, 'F' => Truth.False, _ => Truth.Unknown };

    private static Bar[] TwoBars() => Clock.Closes(Clock.Mon, 10, 10);

    private static IEnumerable<(int A, int B)> Pairs() => Enumerable.Range(0, 3).SelectMany(a => Enumerable.Range(0, 3).Select(b => (a, b)));

    /// <summary>One rig with all the conditions of a family, evaluated after the two bars.</summary>
    private static (Rig Rig, Truth[] Results) Evaluate(IReadOnlyList<string> sources)
    {
        var rig = Rig.Of(Timeframe.M1, sources);
        Truth[] results = [];
        foreach (var bar in TwoBars())
        {
            results = rig.Step(bar);
        }

        return (rig, results);
    }

    [Fact]
    public void The_atoms_have_the_values_the_tables_assume()
    {
        var (_, results) = Evaluate(Atoms);

        results.ShouldBe([Truth.False, Truth.Unknown, Truth.True]);
    }

    [Fact]
    public void And_is_kleenes_table_in_all_nine_cases()
    {
        var pairs = Pairs().ToList();
        var (rig, results) = Evaluate([.. pairs.Select(p => $"({Atoms[p.A]}) and ({Atoms[p.B]})")]);

        for (var i = 0; i < pairs.Count; i++)
        {
            results[i].ShouldBe(Of(AndTable[pairs[i].A][pairs[i].B]), $"{Values[pairs[i].A]} and {Values[pairs[i].B]}");
        }

        ShouldDecideTrueExactlyWhereTrue(rig, results);
    }

    [Fact]
    public void Or_is_kleenes_table_in_all_nine_cases()
    {
        var pairs = Pairs().ToList();
        var (rig, results) = Evaluate([.. pairs.Select(p => $"({Atoms[p.A]}) or ({Atoms[p.B]})")]);

        for (var i = 0; i < pairs.Count; i++)
        {
            results[i].ShouldBe(Of(OrTable[pairs[i].A][pairs[i].B]), $"{Values[pairs[i].A]} or {Values[pairs[i].B]}");
        }

        ShouldDecideTrueExactlyWhereTrue(rig, results);
    }

    [Fact]
    public void Not_is_kleenes_table_in_all_three_cases()
    {
        var (rig, results) = Evaluate([.. Atoms.Select(a => $"not ({a})")]);

        for (var i = 0; i < 3; i++)
        {
            results[i].ShouldBe(Of(NotTable[i][0]), "not " + Values[i]);
        }

        ShouldDecideTrueExactlyWhereTrue(rig, results);
    }

    [Fact]
    public void Equality_of_two_truth_values_is_unknown_when_either_is_unknown()
    {
        var pairs = Pairs().ToList();
        var (rig, results) = Evaluate([.. pairs.Select(p => $"({Atoms[p.A]}) == ({Atoms[p.B]})"), .. pairs.Select(p => $"({Atoms[p.A]}) != ({Atoms[p.B]})")]);

        for (var i = 0; i < pairs.Count; i++)
        {
            results[i].ShouldBe(Of(EqualTable[pairs[i].A][pairs[i].B]), $"{Values[pairs[i].A]} == {Values[pairs[i].B]}");
            results[pairs.Count + i].ShouldBe(Of(NotEqualTable[pairs[i].A][pairs[i].B]), $"{Values[pairs[i].A]} != {Values[pairs[i].B]}");
        }

        ShouldDecideTrueExactlyWhereTrue(rig, results);
    }

    [Fact]
    public void Three_operands_in_every_combination_follow_the_tables_with_and_binding_tighter_than_or()
    {
        var triples = Enumerable.Range(0, 27).Select(n => (A: n / 9, B: n / 3 % 3, C: n % 3)).ToList();
        string Atom(int i) => "(" + Atoms[i] + ")";
        var (rig, results) = Evaluate(
        [
            .. triples.Select(t => $"{Atom(t.A)} and {Atom(t.B)} or {Atom(t.C)}"),
            .. triples.Select(t => $"{Atom(t.A)} or {Atom(t.B)} and {Atom(t.C)}"),
            .. triples.Select(t => $"not {Atom(t.A)} and {Atom(t.B)}"),
            .. triples.Select(t => $"not ({Atom(t.A)} and {Atom(t.B)} or {Atom(t.C)})"),
        ]);

        char And(char a, char b) => AndTable[Index(a)][Index(b)];
        char Or(char a, char b) => OrTable[Index(a)][Index(b)];
        char Not(char a) => NotTable[Index(a)][0];
        for (var i = 0; i < triples.Count; i++)
        {
            var (a, b, c) = (Letter(triples[i].A), Letter(triples[i].B), Letter(triples[i].C));
            results[i].ShouldBe(Of(Or(And(a, b), c)), $"{a} and {b} or {c}");
            results[27 + i].ShouldBe(Of(Or(a, And(b, c))), $"{a} or {b} and {c}");
            results[54 + i].ShouldBe(Of(And(Not(a), b)), $"not {a} and {b}");
            results[81 + i].ShouldBe(Of(Not(Or(And(a, b), c))), $"not ({a} and {b} or {c})");
        }

        ShouldDecideTrueExactlyWhereTrue(rig, results);
    }

    [Fact]
    public void De_morgans_laws_hold_between_compiled_conditions()
    {
        var pairs = Pairs().ToList();
        var (_, results) = Evaluate(
        [
            .. pairs.Select(p => $"not (({Atoms[p.A]}) and ({Atoms[p.B]}))"),
            .. pairs.Select(p => $"not ({Atoms[p.A]}) or not ({Atoms[p.B]})"),
            .. pairs.Select(p => $"not (({Atoms[p.A]}) or ({Atoms[p.B]}))"),
            .. pairs.Select(p => $"not ({Atoms[p.A]}) and not ({Atoms[p.B]})"),
        ]);

        for (var i = 0; i < 9; i++)
        {
            results[9 + i].ShouldBe(results[i], "not (a and b) = not a or not b");
            results[27 + i].ShouldBe(results[18 + i], "not (a or b) = not a and not b");
        }
    }

    [Fact]
    public void An_unknown_is_false_to_the_decision_and_so_is_its_negation()
    {
        var rig = Rig.Of(Timeframe.M1, ["sma(5) > 0", "not (sma(5) > 0)", "not (not (sma(5) > 0))", "sma(5) > 0 or close > 5", "not (sma(5) > 0) and close < 5"]);

        foreach (var bar in TwoBars())
        {
            rig.Step(bar);
        }

        rig.Read().ShouldBe([Truth.Unknown, Truth.Unknown, Truth.Unknown, Truth.True, Truth.False]);
        rig.Bound.Select(b => b.Evaluate(rig.Frame)).ShouldBe([false, false, false, true, false]);
    }

    [Fact]
    public void An_unknown_becomes_a_value_when_the_data_is_ready_and_not_follows_it()
    {
        var rig = Rig.Of(Timeframe.M1, ["sma(5) > 0", "not (sma(5) > 0)", "sma(5) > 100", "not (sma(5) > 100)"]);
        var steps = new List<Truth[]>();

        foreach (var bar in Clock.Closes(Clock.Mon, 10, 10, 10, 10, 10, 10))
        {
            steps.Add(rig.Step(bar));
        }

        steps[3].ShouldBe([Truth.Unknown, Truth.Unknown, Truth.Unknown, Truth.Unknown], "four bars: sma(5) is not ready");
        steps[4].ShouldBe([Truth.True, Truth.False, Truth.False, Truth.True], "the fifth bar makes the average: 10");
        steps[5].ShouldBe([Truth.True, Truth.False, Truth.False, Truth.True]);
    }

    private static char Letter(int index) => "FUT"[index];

    private static int Index(char letter) => "FUT".IndexOf(letter, StringComparison.Ordinal);

    private static void ShouldDecideTrueExactlyWhereTrue(Rig rig, Truth[] results)
    {
        for (var i = 0; i < results.Length; i++)
        {
            rig.Bound[i].Evaluate(rig.Frame).ShouldBe(results[i] == Truth.True, "condition " + i);
        }
    }
}
