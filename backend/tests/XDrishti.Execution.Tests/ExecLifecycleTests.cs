using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

public sealed class ExecLifecycleTests
{
    private static readonly ExecState[] All = Enum.GetValues<ExecState>();

    [Fact]
    public void There_are_14_states_and_exactly_23_of_196_pairs_are_legal()
    {
        All.Length.ShouldBe(14);
        var legal = All.SelectMany(a => All.Select(b => (a, b))).Where(p => ExecLifecycle.IsLegal(p.a, p.b)).ToList();

        legal.Count.ShouldBe(23);
        ExecLifecycle.LegalMoves.Count.ShouldBe(23);
        ExecLifecycle.LegalMoves.Distinct().Count().ShouldBe(23);
    }

    [Theory]
    [InlineData(ExecState.Rejected)]
    [InlineData(ExecState.Cancelled)]
    [InlineData(ExecState.Failed)]
    [InlineData(ExecState.Reconciled)]
    public void Final_states_have_no_move(ExecState final)
    {
        ExecLifecycle.IsFinal(final).ShouldBeTrue();
        ExecLifecycle.LegalFrom(final).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_four_are_final()
    {
        All.Where(ExecLifecycle.IsFinal).ShouldBe([ExecState.Rejected, ExecState.Cancelled, ExecState.Failed, ExecState.Reconciled], ignoreOrder: true);
    }

    [Fact]
    public void An_illegal_pair_is_refused_naming_the_legal_moves()
    {
        var r = ExecLifecycle.Check(ExecState.Closed, ExecState.Open);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe("execution.illegal_move");
        r.Error.Message.ShouldContain("Reconciled");
        ExecLifecycle.Check(ExecState.Planned, ExecState.Armed).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Spot_checks_of_the_table()
    {
        ExecLifecycle.IsLegal(ExecState.Closing, ExecState.Protected).ShouldBeTrue();
        ExecLifecycle.IsLegal(ExecState.Planned, ExecState.Submitting).ShouldBeFalse();
        ExecLifecycle.IsLegal(ExecState.Open, ExecState.Open).ShouldBeFalse();
    }
}
