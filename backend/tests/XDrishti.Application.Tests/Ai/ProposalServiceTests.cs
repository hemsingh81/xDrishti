using XDrishti.Application.Ai.Proposals;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-08 Contract: the service is paused, follows the weekly digest and reports counts and finding codes, never a text.</summary>
public sealed class ProposalServiceTests
{
    [Fact]
    public void The_service_starts_paused_after_the_weekly_digest()
    {
        var definition = ProposalService.Definition;

        definition.Id.ShouldBe("proposals");
        definition.Defaults.Enabled.ShouldBeFalse();
        definition.Constraints.MustFollow.ShouldBe(["weekly"]);
    }

    [Fact]
    public void The_outcome_counts_and_names_codes()
    {
        var run = new ProposalRunView("stored", null, 1, 1, 2, 1, 0, [new ProposalRefusedView("hypothesis", "knob_kind_not_supported")], [], new string('a', 64), false);

        var outcome = ProposalService.Outcome(Result.Success(run));

        outcome.Status.ShouldBe(XDrishti.Domain.Platform.Services.ServiceRunStatus.Succeeded);
        outcome.Message!.ShouldContain("1 stored, 1 refused");
        outcome.Message!.ShouldContain("knob_kind_not_supported");
    }

    [Fact]
    public void An_off_switch_or_a_busy_model_is_skipped_not_failed()
    {
        ProposalService.Outcome(Result.Failure<ProposalRunView>(ProposalErrors.Disabled())).Status.ShouldBe(XDrishti.Domain.Platform.Services.ServiceRunStatus.Skipped);
        ProposalService.Outcome(Result.Failure<ProposalRunView>(ProposalErrors.Busy())).Status.ShouldBe(XDrishti.Domain.Platform.Services.ServiceRunStatus.Skipped);
        ProposalService.Outcome(Result.Failure<ProposalRunView>(ProposalErrors.Invalid("x"))).Status.ShouldBe(XDrishti.Domain.Platform.Services.ServiceRunStatus.Failed);
    }
}
