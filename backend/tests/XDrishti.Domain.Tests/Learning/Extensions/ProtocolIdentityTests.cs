using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>The stored record of a protocol (P15-10 criteria 4 and 7): the same inputs give the same id, a different input a different one, and the record never holds a number for a non-value.</summary>
public sealed class ProtocolIdentityTests
{
    private static ProtocolIdentity Identity(int trials = 3, bool hold = false, string calendar = "c1") =>
        new("filter:results", "F", null, "evf-1:N=2", "none", new string('b', 64), null, new string('c', 64), trials, "quarter", new Dictionary<string, string> { ["calendar_sha256"] = calendar, ["engine"] = "sim-8" }, hold);

    private static ProtocolOutcome Outcome() => ProtocolL.Evaluate(null);

    [Fact]
    public void The_same_inputs_give_the_same_id_in_any_source_order()
    {
        var a = Identity().ToRecord(Outcome());
        var reversed = Identity() with { Sources = new Dictionary<string, string> { ["engine"] = "sim-8", ["calendar_sha256"] = "c1" } };

        reversed.ToRecord(Outcome()).EvaluationId.ShouldBe(a.EvaluationId);
        a.EvaluationId.Length.ShouldBe(64);
    }

    [Fact]
    public void Another_calendar_another_count_or_the_hold_out_flag_is_another_record()
    {
        var a = Identity().ToRecord(Outcome()).EvaluationId;

        Identity(calendar: "c2").ToRecord(Outcome()).EvaluationId.ShouldNotBe(a);
        Identity(trials: 4).ToRecord(Outcome()).EvaluationId.ShouldNotBe(a);
        Identity(hold: true).ToRecord(Outcome()).EvaluationId.ShouldNotBe(a);
    }

    [Fact]
    public void The_record_carries_the_verdict_the_missing_quantities_the_factor_and_the_sources()
    {
        var record = Identity().ToRecord(Outcome());

        record.Verdict.ShouldBe("NOT_EVALUABLE");
        record.Missing.ShouldBe(["no accepted eval run with set D"]);
        record.Factor.ShouldBe(1 + (0.25 * Math.Log2(3)), 1e-12);
        record.InputsJson.ShouldContain("\"calendar_sha256\":\"c1\"");
        record.HoldOutRead.ShouldBeFalse();
    }
}
