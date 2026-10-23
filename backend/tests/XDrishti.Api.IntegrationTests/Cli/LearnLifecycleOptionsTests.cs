using XDrishti.Api.Cli;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn lifecycle</c> (spec P6-08): pure parsing, no database.</summary>
public sealed class LearnLifecycleOptionsTests
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    [Fact]
    public void A_move_carries_the_state_the_facts_and_the_evidence()
    {
        var (options, errors) = LearnLifecycleOptions.Parse(["--cell", Cell, "--to", "active", "--reason", "paper ok", "--evidence", "run:a, run:b", "--owner-approved", "--within-band", "--paper-days", "21", "--after-ref", "v3"]);

        errors.ShouldBeEmpty();
        options!.To.ShouldBe(CellState.Active);
        options.Evidence.ShouldBe(["run:a", "run:b"]);
        options.Facts.ShouldBe(new TransitionFacts(OwnerApproved: true, WithinBand: true, PaperDays: 21));
        options.AfterRef.ShouldBe("v3");
        options.RevertId.ShouldBeNull();
    }

    [Fact]
    public void A_revert_takes_a_row_and_a_reason_only()
    {
        var (ok, _) = LearnLifecycleOptions.Parse(["--revert", "7", "--reason", "wrong"]);
        var (mixed, errors) = LearnLifecycleOptions.Parse(["--revert", "7", "--reason", "wrong", "--to", "paper"]);

        ok!.RevertId.ShouldBe(7);
        mixed.ShouldBeNull();
        errors.ShouldContain("--revert takes only --reason");
    }

    [Theory]
    [InlineData("--cell", "x", "--to", "paper", "--reason", "r")]
    [InlineData("--cell", Cell, "--to", "flying", "--reason", "r")]
    [InlineData("--cell", Cell, "--to", "paper")]
    [InlineData("--cell", Cell, "--to", "paper", "--reason", "r", "--paper-days", "-1")]
    [InlineData("--revert", "0", "--reason", "r")]
    [InlineData("--cell", Cell, "--to", "paper", "--reason", "r", "--nope")]
    [InlineData("--cell", Cell, "--to", "paper", "--reason", "r", "--owner-approved", "--owner-approved")]
    public void Wrong_command_lines_are_listed_not_run(params string[] args)
    {
        var (options, errors) = LearnLifecycleOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
