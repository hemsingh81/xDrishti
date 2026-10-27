using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 6: the idea assertions on two hand-written outcomes.</summary>
public sealed class DraftGradingTests
{
    private const string Tree = """{"strategy":{"id":"VWAP_RSI","sides":["BUY","SELL"],"stop":{"type":"swing_low","lookback":"5"},"exits":[{"policy":"FIXED_RR","rr":["2.0"]}],"time_rules":{"square_off":"15:15"}}}""";

    private const string Yaml = "strategy:\n  id: VWAP_RSI\n  sides: [BUY, SELL]\n  setup: { all: [\"rsi(14) > 55\", \"close > vwap\"] }\n  stop: { type: swing_low, lookback: 5 }\n  exits: [{ policy: FIXED_RR, rr: [2.0] }]\n  time_rules: { square_off: \"15:15\" }\n";

    private static IdeaCase Draft(params IdeaAssertion[] assertions) => new("ID-T", "an idea", IdeaCase.Draft, assertions);

    private static DraftAnswer Answer(bool decline = false, string[]? unsupported = null, DraftNotExpressed[]? notExpressed = null) =>
        new(decline, unsupported ?? [], [], [], notExpressed ?? [], DraftRequestedTest.None, decline ? null : Yaml);

    [Fact]
    public void A_valid_draft_that_meets_every_assertion_has_full_intent()
    {
        var idea = Draft(new PathEquals("strategy.stop.type", "swing_low"), new YamlContainsAny(["vwap"]), new NumberCovered("55"), new NumberCovered("15:15"), new SidesInclude(["BUY", "SELL"]), new PathExists("strategy.exits[0].rr"), new PathEquals("strategy.exits[0].rr[0]", "2"));
        var outcome = new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, Answer());

        var grade = DraftGrader.Grade(idea, outcome);

        grade.Accepted.ShouldBeTrue();
        (grade.PassedAssertions, grade.TotalAssertions).ShouldBe((7, 7));
        grade.Intent.ShouldBe(1.0);
        grade.DeclineCorrect.ShouldBeNull();
    }

    [Fact]
    public void A_failed_assertion_lowers_the_intent_and_is_named()
    {
        var idea = Draft(new PathEquals("strategy.stop.type", "fixed_pct"), new YamlContainsAny(["bollinger"]), new NumberCovered("70"), new PathExists("strategy.sell"));

        var grade = DraftGrader.Grade(idea, new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, Answer()));

        grade.PassedAssertions.ShouldBe(0);
        grade.Failed.ShouldBe(["path_equals:strategy.stop.type", "yaml_contains_any", "number_covered:70", "path_exists:strategy.sell"]);
        grade.Intent.ShouldBe(0.0);
    }

    [Fact]
    public void A_number_of_the_idea_listed_as_not_expressed_is_covered()
    {
        var idea = Draft(new NumberCovered("70"));
        var answer = Answer(notExpressed: [new DraftNotExpressed("exit when RSI(2) is above 70", "signal exits are not in the language")]);

        DraftGrader.Grade(idea, new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, answer)).PassedAssertions.ShouldBe(1);
    }

    [Fact]
    public void An_invalid_draft_is_not_accepted_and_no_tree_fails_the_path_assertions()
    {
        var idea = Draft(new PathExists("strategy.stop"), new YamlContainsAny(["vwap"]));

        var grade = DraftGrader.Grade(idea, new IdeaOutcome(DraftStatus.Invalid, Yaml, null, Answer()));

        grade.Accepted.ShouldBeFalse();
        grade.PassedAssertions.ShouldBe(1);
    }

    [Fact]
    public void A_decline_that_names_the_unsupported_part_is_correct_and_a_valid_text_for_a_decline_idea_is_not()
    {
        var idea = new IdeaCase("ID-09", "buy on good news", IdeaCase.Decline, [new DeclineAsserted(), new UnsupportedMentions(["news", "results"])]);
        var declined = new IdeaOutcome(DraftStatus.Declined, null, null, Answer(decline: true, unsupported: ["news and results are not available"]));
        var drafted = new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, Answer());

        var good = DraftGrader.Grade(idea, declined);
        var bad = DraftGrader.Grade(idea, drafted);

        (good.Accepted, good.DeclineCorrect, good.Intent).ShouldBe((true, true, 1.0));
        (bad.Accepted, bad.DeclineCorrect).ShouldBe((false, false));
    }

    [Fact]
    public void The_partial_kind_accepts_a_decline_or_a_valid_draft_that_lists_the_unsupported_part()
    {
        var idea = new IdeaCase("ID-10", "sector rs and vix", IdeaCase.DeclineOrPartial, [new UnsupportedMentions(["sector", "vix"])]);
        var partial = new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, Answer(notExpressed: [new DraftNotExpressed("sector relative strength", "sector_rs is not built")]));
        var silent = new IdeaOutcome(DraftStatus.Valid, Yaml, Tree, Answer());
        var declined = new IdeaOutcome(DraftStatus.Declined, null, null, Answer(decline: true, unsupported: ["the VIX is not available"]));

        DraftGrader.Grade(idea, partial).DeclineCorrect.ShouldBe(true);
        DraftGrader.Grade(idea, silent).DeclineCorrect.ShouldBe(false);
        DraftGrader.Grade(idea, declined).DeclineCorrect.ShouldBe(true);
    }

    [Fact]
    public void Sides_come_from_the_list_and_a_sell_side_may_be_a_sell_block()
    {
        const string OnlyBuy = """{"strategy":{"sides":["BUY"]}}""";
        const string SellBlock = """{"strategy":{"sell":{"setup":"x"}}}""";
        var sell = Draft(new SidesInclude(["SELL"]));

        DraftGrader.Grade(sell, new IdeaOutcome(DraftStatus.Valid, Yaml, OnlyBuy, Answer())).PassedAssertions.ShouldBe(0);
        DraftGrader.Grade(sell, new IdeaOutcome(DraftStatus.Valid, Yaml, SellBlock, Answer())).PassedAssertions.ShouldBe(1);
    }
}
