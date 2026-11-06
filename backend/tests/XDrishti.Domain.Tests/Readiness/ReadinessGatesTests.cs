using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1, AC9 (ADR 0221): Gate R needs rows 1 to 12 and 14; Gate S needs Gate R, rows 13 and 15 and the label "Ready for the next phase"; an unavailable row blocks.</summary>
public sealed class ReadinessGatesTests
{
    private static readonly ReadinessOptions Options = ReadinessInputsFixture.Options;

    private static ReadinessAssessment Assess(ChecklistInputs checklist) => ReadinessAssessment.Of(checklist, ReadinessInputsFixture.Perfect(), HardFailInputs.AllClear, Options);

    [Fact]
    public void With_every_row_passing_and_a_perfect_score_both_gates_pass()
    {
        var assessment = Assess(ReadinessInputsFixture.Passing());

        assessment.GateR.Passed.ShouldBeTrue();
        assessment.GateS.Passed.ShouldBeTrue();
        assessment.GateR.Failing.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(13)]
    [InlineData(15)]
    public void Row_13_or_15_failing_does_not_fail_gate_R_but_fails_gate_S(int row)
    {
        var inputs = row == 13 ? ReadinessInputsFixture.Passing() with { BenchmarkBeaten = false } : ReadinessInputsFixture.Passing() with { PhaseCheckMet = false };

        var assessment = Assess(inputs);

        assessment.GateR.Passed.ShouldBeTrue();
        assessment.GateS.Passed.ShouldBeFalse();
        assessment.GateS.Failing.Select(r => r.Number).ShouldBe([row]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(14)]
    public void A_required_row_failing_fails_both_gates_and_is_named(int row)
    {
        var inputs = row switch
        {
            1 => ReadinessInputsFixture.Passing() with { Registered = false },
            3 => ReadinessInputsFixture.Passing() with { ExpectancyR = 0.25, Factor = 1.75 }, // 0.25 is under 0.2625 (row 3) and the t of 4.47 is over 3.5 (row 6)
            7 => ReadinessInputsFixture.Passing() with { RandomEntry = RowStatus.Fail },
            12 => ReadinessInputsFixture.Passing() with { HoldOut = RowStatus.Fail },
            _ => ReadinessInputsFixture.Passing() with { CapacityOk = false },
        };

        var assessment = Assess(inputs);

        assessment.GateR.Passed.ShouldBeFalse();
        assessment.GateS.Passed.ShouldBeFalse();
        assessment.GateR.Failing.Select(r => r.Number).ShouldBe([row]);
    }

    [Fact]
    public void An_unavailable_row_blocks_the_gate()
    {
        var assessment = Assess(ReadinessInputsFixture.Passing() with { Plateau = RowStatus.Unavailable });

        assessment.GateR.Passed.ShouldBeFalse();
        assessment.GateR.Failing.Single().Status.ShouldBe(RowStatus.Unavailable);
    }

    [Fact]
    public void Gate_S_needs_the_ready_label_even_when_every_row_passes()
    {
        var weak = ReadinessInputsFixture.Perfect() with { EcePp = 6 };

        var assessment = ReadinessAssessment.Of(ReadinessInputsFixture.Passing(), weak, HardFailInputs.AllClear, Options);

        assessment.GateR.Passed.ShouldBeTrue();
        assessment.GateS.LabelOk.ShouldBeFalse();
        assessment.GateS.Passed.ShouldBeFalse();
        assessment.GateS.Failing.ShouldBeEmpty("the rows pass; the label is what stops it");
    }

    [Fact]
    public void A_row_missing_from_the_list_counts_as_unavailable()
    {
        var result = ReadinessGates.GateR([]);

        result.Passed.ShouldBeFalse();
        result.Failing.Count.ShouldBe(13);
        result.Failing.ShouldAllBe(r => r.Status == RowStatus.Unavailable);
    }

    [Fact]
    public void Gate_names_are_R_and_S()
    {
        var assessment = Assess(ReadinessInputsFixture.Passing());

        (assessment.GateR.Name, assessment.GateS.Name).ShouldBe(("R", "S"));
    }
}
