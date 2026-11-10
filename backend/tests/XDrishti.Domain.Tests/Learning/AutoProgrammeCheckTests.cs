using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

public sealed class AutoProgrammeCheckTests
{
    private static readonly AutoProgrammeFacts AllMet = new(3, 1, true, 5, 1, 1, 10, 0, 2, 1);

    private static string Verdict(AutoProgrammeFacts facts, string id) => AutoProgrammeCheck.Evaluate(facts).Items.Single(i => i.Id == id).Verdict;

    [Fact]
    public void Every_item_met_makes_the_programme_met()
    {
        AutoProgrammeCheck.Evaluate(AllMet).Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    public static TheoryData<string, AutoProgrammeFacts, string> Cases => new()
    {
        { "recovery", AllMet with { RecoveryPasses = null }, PhaseVerdicts.NotEvaluable },
        { "recovery", AllMet with { RecoveryPasses = 0, MultiDayRecoveries = 0 }, PhaseVerdicts.NotEvaluable },
        { "recovery", AllMet with { MultiDayRecoveries = 0 }, PhaseVerdicts.NotMet },
        { "removal", AllMet with { RemovalAdrPresent = null }, PhaseVerdicts.NotEvaluable },
        { "removal", AllMet with { RemovalAdrPresent = false }, PhaseVerdicts.NotMet },
        { "essential", AllMet with { EssentialSettings = null }, PhaseVerdicts.NotEvaluable },
        { "essential", AllMet with { EssentialSettings = 0 }, PhaseVerdicts.NotEvaluable },
        { "essential", AllMet with { EssentialSettings = 8 }, PhaseVerdicts.Met },
        { "essential", AllMet with { EssentialSettings = 9 }, PhaseVerdicts.NotMet },
        { "job", AllMet with { FenceBreaches = null }, PhaseVerdicts.NotEvaluable },
        { "job", AllMet with { Jobs = 0, FinishedJobs = 0, FinishedJobChunks = 0 }, PhaseVerdicts.NotEvaluable },
        { "job", AllMet with { Jobs = 50, FinishedJobs = 0, FinishedJobChunks = 0 }, PhaseVerdicts.NotEvaluable },
        { "job", AllMet with { FenceBreaches = 1 }, PhaseVerdicts.NotMet },
        { "job", AllMet with { FinishedJobs = 0, FinishedJobChunks = 0 }, PhaseVerdicts.NotMet },
        { "job", AllMet with { FinishedJobChunks = 0 }, PhaseVerdicts.NotMet },
        { "suggestions", AllMet with { AutoOnNightsWithSuggestions = null }, PhaseVerdicts.NotEvaluable },
        { "suggestions", AllMet with { SwitchChanges = 0, AutoOnNightsWithSuggestions = 0 }, PhaseVerdicts.NotEvaluable },
        { "suggestions", AllMet with { AutoOnNightsWithSuggestions = 0 }, PhaseVerdicts.NotMet },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_item_has_its_verdict_per_state(string id, AutoProgrammeFacts facts, string expected)
    {
        Verdict(facts, id).ShouldBe(expected);
    }

    [Fact]
    public void An_unreadable_item_is_never_met_and_makes_the_programme_not_evaluable()
    {
        var report = AutoProgrammeCheck.Evaluate(AllMet with { RemovalAdrPresent = null });

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void A_failed_item_makes_the_programme_not_met_even_beside_an_unreadable_one()
    {
        var report = AutoProgrammeCheck.Evaluate(AllMet with { RemovalAdrPresent = null, EssentialSettings = 12 });

        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public void The_text_names_every_item_and_prints_the_limits()
    {
        var text = AutoProgrammeCheck.Render(AutoProgrammeCheck.Evaluate(AllMet));

        text.ShouldContain("AU programme check: MET");
        text.ShouldContain("Limits:");
        text.ShouldContain("places no order");
    }
}
