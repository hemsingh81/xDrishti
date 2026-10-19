namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 3 of P3-08, the engine's third of the three-way agreement: the hand rows are the oracle author's own data (minutes, candidate, rules and the numbers copied from the spec tables, <c>AllRows</c>); the oracle equals those
/// numbers in <c>HandRowTests</c>, and here the engine, fed the same data through <see cref="OracleAdapter"/>, equals the same numbers, in both fill modes (with no slippage the filled prices are the exact ones) - and the whole result of
/// the engine equals the oracle's on every compared field, not only on the fields the spec table prints.
/// </summary>
public sealed class EngineHandRowTests
{
    internal static readonly RatioStats Stats = new();

    private static (OracleResult Engine, IReadOnlyList<Mismatch> Echoes) EngineOf(HandRow row, bool exactMode)
    {
        OracleRules rules = RowCheck.RulesOf(row, exactMode);
        var walked = OracleAdapter.Walk(OracleAdapter.Bars(row.Bars), [OracleAdapter.Candidate(row.Candidate)], OracleAdapter.Rules(rules));
        return (walked.Results[0], walked.Echoes);
    }

    private static void Check(string id, bool exactMode)
    {
        HandRow row = AllRows.Get(id);
        var (engine, echoes) = EngineOf(row, exactMode);

        // 1. the engine against the numbers copied from the spec table (the same comparison the oracle passes in HandRowTests)
        List<string> bad = RowCheck.Compare(row.Expect, engine, exactMode);
        bad.ShouldBeEmpty("engine against the spec numbers: " + string.Join("; ", bad));

        // 2. the engine against the oracle on every compared field (the oracle itself is held to the spec numbers by HandRowTests)
        OracleResult oracle = ReferenceWalker.Walk(row.Bars, row.Candidate, RowCheck.RulesOf(row, exactMode), OracleFaults.None);
        List<Mismatch> differences = OracleComparer.Diff(oracle, engine, Stats, id);
        differences.ShouldBeEmpty("engine against the oracle: " + string.Join("; ", differences));
        echoes.ShouldBeEmpty("pass-through fields: " + string.Join("; ", echoes));
    }

    public static TheoryData<string> Ids() => AllRows.Ids();

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_engine_equals_the_spec_numbers_and_the_oracle_in_the_modelled_mode(string id)
    {
        Check(id, exactMode: false);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_engine_equals_the_spec_numbers_and_the_oracle_with_no_slippage(string id)
    {
        Check(id, exactMode: true);
    }

    [Fact]
    public void The_hand_rows_are_the_authors_hundred_and_the_comparison_covers_all_of_them()
    {
        AllRows.Rows.Count.ShouldBe(100);
        AllRows.Rows.Select(r => r.Id).Distinct().Count().ShouldBe(100);
    }

    [Fact]
    public void The_sentinel_a_wrong_expected_number_is_caught_for_the_engine_too()
    {
        // a copied number that is off by a tick must fail the same comparison, so that a pass is not a comparison that cannot fail
        HandRow row = AllRows.Get("F1");
        var (engine, _) = EngineOf(row, exactMode: true);
        var wrong = new Expect { Entry = row.Expect.Entry + 0.05m, Exit = row.Expect.Exit, Reason = row.Expect.Reason, R = row.Expect.R, Mae = row.Expect.Mae, Mfe = row.Expect.Mfe, Target = row.Expect.Target, Bars = row.Expect.Bars };

        RowCheck.Compare(wrong, engine, exactMode: true).ShouldNotBeEmpty();
    }
}
