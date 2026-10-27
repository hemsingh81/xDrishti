using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Domain.Tests.Assistant.Drafting;

/// <summary>Spec P9-07 AC 3 and 4 and the unit test plan: the number extraction and the checks C3 to C6, each with a passing and a failing row.</summary>
public sealed class DraftChecksTests
{
    private const string Idea = "Intraday: after 9:45 buy when a 15-min candle closes above VWAP with RSI(14) above 55 and 1-hour EMA20 above EMA50. Stop below last swing low. Target 1:2. Exit by 15:15. Mirror for shorts.";

    private const string Tree = """{"id":"X","params":{"rr":"2","rsi_len":"14"},"exits":[{"policy":"FIXED_RR","rr":"2"}],"time_rules":{"first_entry":"09:45","square_off":"15:15"}}""";

    private const string Yaml = "id: X\ntimeframes: [15m]\nsetup: tf(\"1h\").ema(20) > tf(\"1h\").ema(50)\ntrigger: close > vwap and rsi(14) > 55\nrr: 2\nfirst_entry: \"09:45\"\nsquare_off: \"15:15\"";

    private static DraftAnswer Answer(IReadOnlyList<DraftAssumption>? assumptions = null, IReadOnlyList<string>? understood = null, IReadOnlyList<DraftNotExpressed>? notExpressed = null, DraftRequestedTest? test = null) =>
        new(false, [], understood ?? [], assumptions ?? [], notExpressed ?? [], test ?? DraftRequestedTest.None, Yaml);

    private static DraftYamlFacts Facts(string? tree = Tree, string yaml = Yaml, int? version = null, string? id = "X", int parms = 2) => new(yaml, tree, version, id, parms);

    [Fact]
    public void The_numbers_of_the_idea_include_times_the_ratio_the_periods_and_the_15_of_15_min()
    {
        var numbers = DraftIdea.Numbers(Idea);

        numbers.ShouldBe(["9:45", "15", "14", "55", "1", "20", "50", "1:2", "15:15"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("09:45", "9:45")]
    [InlineData("0.50", "0.5")]
    [InlineData("2022", "2022")]
    [InlineData("10.0", "10")]
    public void Tokens_are_normalised(string text, string token) => DraftIdea.Numbers(text).ShouldBe([token]);

    [Fact]
    public void A_ratio_is_told_from_a_time_by_its_second_part()
    {
        DraftIdea.IsRatio("1:2").ShouldBeTrue();
        DraftIdea.IsRatio("9:45").ShouldBeFalse();
        DraftIdea.RatioSecond("1:2").ShouldBe("2");
    }

    [Fact]
    public void The_example_idea_with_a_faithful_answer_has_no_finding() =>
        DraftChecks.Run(Idea, Answer(), Facts()).ShouldBeEmpty();

    [Fact]
    public void C3_passes_for_a_true_assumption_and_fails_for_a_false_or_missing_one()
    {
        DraftChecks.Assumptions(Answer([new DraftAssumption("exits[0].rr", "2.0", "ratio")]), Facts()).ShouldBeEmpty();
        DraftChecks.Assumptions(Answer([new DraftAssumption("params.rsi_len", "14", "idea")]), Facts()).ShouldBeEmpty();
        DraftChecks.Assumptions(Answer([new DraftAssumption("exits[0].rr", "3", "x")]), Facts()).Single().Code.ShouldBe(DraftCodes.AssumptionNotInYaml);
        DraftChecks.Assumptions(Answer([new DraftAssumption("exits[4].rr", "2", "x")]), Facts()).Single().Detail.ShouldBe("exits[4].rr");
        DraftChecks.Assumptions(Answer([new DraftAssumption("a.b.c", "2", "x")]), Facts()).ShouldNotBeEmpty();
        DraftChecks.Assumptions(Answer([new DraftAssumption("exits", "2", "x")]), Facts()).ShouldNotBeEmpty();
    }

    [Fact]
    public void C4_refuses_a_version_a_bad_id_and_too_many_params()
    {
        DraftChecks.Draftness(Facts(version: 1)).Single().Code.ShouldBe(DraftCodes.HasVersion);
        DraftChecks.Draftness(Facts(id: "1bad")).Single().Code.ShouldBe(DraftCodes.IdInvalid);
        DraftChecks.Draftness(Facts(parms: 5)).Single().Code.ShouldBe(DraftCodes.TooManyParams);
        DraftChecks.Draftness(Facts(parms: 4)).ShouldBeEmpty();
    }

    [Fact]
    public void C5_passes_for_numbers_from_the_idea_or_the_text_and_fails_for_an_invented_one()
    {
        DraftChecks.ExplanationNumbers(Idea, Answer(understood: ["RSI(14) above 55", "target of 2 times the risk"]), Facts()).ShouldBeEmpty();
        DraftChecks.ExplanationNumbers(Idea, Answer(understood: ["stop at 3 percent"]), Facts()).Single().ShouldBe(new DraftFinding(DraftCodes.ExplanationNumberNotInSource, "3"));
        DraftChecks.ExplanationNumbers(Idea, Answer([new DraftAssumption("exits[0].rr", "2", "chosen from 77 trades")]), Facts()).Single().Detail.ShouldBe("77");
    }

    [Fact]
    public void C6_fails_for_a_dropped_number_and_passes_when_it_is_listed_or_requested()
    {
        const string Dropped = "Buy above VWAP with RSI(14) above 55 and 2022 data with 33 percent";

        DraftChecks.DroppedNumbers(Dropped, Answer(), Facts()).Select(f => f.Detail).ShouldBe(["2022", "33"]);
        DraftChecks.DroppedNumbers(Dropped, Answer(notExpressed: [new DraftNotExpressed("33 percent filter", "not in the language")], test: new DraftRequestedTest("2022", null, [])), Facts()).ShouldBeEmpty();
    }

    [Fact]
    public void C6_accepts_a_ratio_when_its_second_part_is_in_the_text_and_a_time_only_as_a_time()
    {
        DraftChecks.DroppedNumbers("Target 1:2", Answer(), Facts()).ShouldBeEmpty();
        DraftChecks.DroppedNumbers("Exit by 14:30", Answer(), Facts()).Single().Detail.ShouldBe("14:30");
    }

    [Fact]
    public void An_unreadable_text_is_a_finding_and_the_other_checks_still_run()
    {
        var findings = DraftChecks.Run(Idea, Answer(), Facts(tree: null));

        findings.ShouldContain(f => f.Code == DraftCodes.YamlUnreadable);
    }

    [Fact]
    public void Paths_are_read_inside_the_strategy_key_of_the_file()
    {
        const string Wrapped = """{"strategy":{"id":"X","params":{"rr":"2"},"exits":[{"policy":"FIXED_RR","rr":"2"}]}}""";

        DraftChecks.Assumptions(Answer([new DraftAssumption("exits[0].rr", "2", "ratio"), new DraftAssumption("params.rr", "2", "ratio")]), Facts(Wrapped)).ShouldBeEmpty();
        DraftChecks.Assumptions(Answer([new DraftAssumption("params.rr", "5", "ratio")]), Facts(Wrapped)).ShouldNotBeEmpty();
    }
}
