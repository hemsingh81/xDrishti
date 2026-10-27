using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 5 and 6: the plan and idea files of the repo read, hold ten cases each, and the plans are consistent with the guard.</summary>
public sealed class EvalSetFilesTests
{
    private static readonly IReadOnlyList<PlanCase> Plans = Read(EvalSetReader.ReadPlans(EvalFiles.Text("sets/plans.jsonl")));

    private static readonly IReadOnlyList<IdeaCase> Ideas = Read(EvalSetReader.ReadIdeas(EvalFiles.Text("sets/ideas.jsonl")));

    private static IReadOnlyList<T> Read<T>(XDrishti.Domain.Common.Result<IReadOnlyList<T>> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void There_are_ten_plans_and_ten_ideas_with_the_ids_of_the_spec()
    {
        Plans.Select(p => p.Id).ShouldBe(Enumerable.Range(1, 10).Select(i => $"PL-{i:D2}"));
        Ideas.Select(i => i.Id).ShouldBe(Enumerable.Range(1, 10).Select(i => $"ID-{i:D2}"));
        Ideas.Where(i => i.IsDeclineKind).Select(i => i.Id).ShouldBe(["ID-09", "ID-10"]);
    }

    [Fact]
    public void Each_plan_expects_the_tickets_of_its_facts_and_the_deterministic_text_passes_the_guard()
    {
        foreach (var plan in Plans)
        {
            plan.ExpectTickets.ShouldBe(plan.Facts.Tickets, plan.Id);
            var text = DeterministicBriefing.Build(plan.Facts);
            TextGuard.Check(text, plan.Facts, 300).Findings.ShouldBeEmpty(plan.Id + ": " + text);
            IndependentNumberAudit.Absent(text, plan.Facts).ShouldBeEmpty(plan.Id);
            plan.ExpectFlags.Contains("provisional").ShouldBe(plan.Facts.Provisional, plan.Id);
        }
    }

    [Fact]
    public void The_plans_cover_the_ten_variants()
    {
        Plans[1].Facts.Tickets.ShouldBeEmpty();
        Plans[0].Facts.Tickets.Count.ShouldBe(4);
        Plans[4].Facts.Provisional.ShouldBeTrue();
        Plans[5].Facts.Tickets.Count.ShouldBe(5);
        Plans[6].Facts.Tickets.ShouldBe(["M&M", "BAJAJ-AUTO"]);
        Plans[7].Facts.Lines.Any(l => l.Text.Contains("-0.04R", StringComparison.Ordinal)).ShouldBeTrue();
        Plans[8].Facts.Lines.Any(l => l.Text.Contains("2,49,93,750", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public void Every_idea_has_assertions_and_the_two_declines_name_what_is_unsupported()
    {
        Ideas.ShouldAllBe(i => i.Assertions.Count >= 1);
        Ideas[8].Assertions.OfType<DeclineAsserted>().Count().ShouldBe(1);
        Ideas[9].Kind.ShouldBe(IdeaCase.DeclineOrPartial);
        Ideas[0].Assertions.OfType<NumberCovered>().Select(a => a.Token).ShouldBe(["55", "15:15"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"id":"ID-1","idea":"x","kind":"strange","assert":[{"decline":true}]}""")]
    [InlineData("""{"id":"ID-1","idea":"x","kind":"draft","assert":[{"nonsense":1}]}""")]
    [InlineData("""{"id":"ID-1","idea":"x","kind":"draft","assert":[]}""")]
    public void A_line_that_is_not_an_idea_is_refused_with_its_number(string line) =>
        EvalSetReader.ReadIdeas("\n" + line).Error.Message.ShouldBe("line 2 is not a valid case");
}
