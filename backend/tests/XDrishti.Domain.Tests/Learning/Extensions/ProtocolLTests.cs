using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Protocol L of P15-10 (the flag is read from the eval run, never recomputed): KEEP iff passed, DROP iff a gating run with 200 or more rows failed, NOT_EVALUABLE otherwise.</summary>
public sealed class ProtocolLTests
{
    private static FlagGrade Grade(int labels = 240, bool gating = true, bool passed = true) => new("run-1", "fp-1", labels, gating, passed, "model-hash", "prompt-hash");

    [Fact]
    public void A_pass_with_enough_labels_is_a_keep_and_links_the_eval_run()
    {
        var outcome = ProtocolL.Evaluate(Grade());

        outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        outcome.EstimatesJson.ShouldContain("\"eval_run_id\":\"run-1\"");
        outcome.EstimatesJson.ShouldContain("\"fingerprint\":\"fp-1\"");
    }

    [Fact]
    public void A_gating_failure_with_enough_labels_is_a_drop()
    {
        ProtocolL.Evaluate(Grade(passed: false)).Verdict.ShouldBe(ExtensionVerdict.Drop);
    }

    [Fact]
    public void A_failure_of_a_run_that_is_not_gating_is_not_evaluable()
    {
        var outcome = ProtocolL.Evaluate(Grade(gating: false, passed: false));

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["the newest run is not a gating run"]);
    }

    [Theory]
    [InlineData(199, true)]
    [InlineData(0, false)]
    public void Fewer_than_two_hundred_labels_is_not_evaluable_whatever_the_grade_says(int labels, bool passed)
    {
        var outcome = ProtocolL.Evaluate(Grade(labels, passed: passed));

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe([$"labels {labels} of 200"]);
    }

    [Fact]
    public void No_accepted_eval_run_is_not_evaluable_with_no_estimate()
    {
        var outcome = ProtocolL.Evaluate(null);

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["no accepted eval run with set D"]);
        outcome.EstimatesJson.ShouldBe("{\"status\":\"unavailable\"}");
    }
}
