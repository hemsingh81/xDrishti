using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// CF-10 part 10d-1 review (ADR 0220): <c>xd learn designate</c> stays the owner's local way to name the first champion (OWNER DECISION NEEDED: it checks only the champion rule while the evidence sources of D7 do not exist), but it refuses to name as champion a model that has a rollback
/// proposal, in any state, with a message that points to the promote route; every other designation is the P6-07 one.
/// </summary>
public sealed class OwnerDesignateModelHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OwnerDesignateModelHandler Handler(MlRollbackFixture f) => new(f.Registry, f.Store, f.Promote, f.Designate, NullLogger<OwnerDesignateModelHandler>.Instance);

    private static void Propose(MlRollbackFixture f, long model, string state)
    {
        f.Store.Rows.Add(new RollbackProposalRow(f.Store.Rows.Count + 1, new NewRollbackProposal(model, "calibration", 1, 1, "t", [], null, false, new string('a', 64)), new DateOnly(2026, 11, 1), f.Time.GetUtcNow(), state, state == "proposed" ? null : "owner:me@x.test", null, null));
    }

    [Fact]
    public async Task A_model_without_a_rollback_proposal_is_named_champion_as_before_on_the_champion_rule_alone()
    {
        var f = new MlRollbackFixture();
        f.Add(7, ModelRole.Challenger);

        var result = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(7, ModelRole.Champion, "retrained", "cli:xd"), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        f.Registry.Designations.Single().Actor.ShouldBe("cli:xd");
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("applied")]
    [InlineData("dismissed")]
    public async Task A_model_with_a_rollback_proposal_in_any_state_is_refused_as_champion_with_a_message_that_names_the_promote_route(string state)
    {
        var f = new MlRollbackFixture();
        f.Add(7, ModelRole.Challenger);
        Propose(f, 7, state);

        var result = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(7, ModelRole.Champion, "retrained", "cli:xd"), Ct);

        result.Error.Code.ShouldBe(MlSafetyErrors.DesignateRolledBackCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Message.ShouldContain("/api/learning/models/7/promote");
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
        f.Registry.Designations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_model_that_was_just_rolled_back_by_the_automatic_setting_cannot_be_named_champion_from_the_command_line()
    {
        var f = new MlRollbackFixture { Inputs = MlRollbackFixture.FiringCalibration(), Switches = MlRollbackFixture.On(rollback: true) };
        f.Add(7, ModelRole.Champion);
        await f.ProposeAsync(7);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);

        var result = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(7, ModelRole.Champion, "back", "cli:xd"), Ct);

        result.Error.Code.ShouldBe(MlSafetyErrors.DesignateRolledBackCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Challenger);
    }

    [Fact]
    public async Task Other_roles_are_not_affected_by_a_rollback_proposal_and_another_models_proposal_is_not_this_models()
    {
        var f = new MlRollbackFixture();
        f.Add(7, ModelRole.Challenger);
        f.Add(8, ModelRole.Challenger, 'b');
        Propose(f, 7, "applied");

        var retire = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(7, ModelRole.Retired, "set aside", "cli:xd"), Ct);
        var other = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(8, ModelRole.Champion, "fine", "cli:xd"), Ct);

        retire.IsSuccess.ShouldBeTrue();
        other.IsSuccess.ShouldBeTrue();
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Retired);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Champion);
    }

    [Fact]
    public async Task Naming_a_champion_while_another_is_champion_goes_through_promotion_and_is_refused_with_its_reasons()
    {
        var f = new MlRollbackFixture();
        f.Add(7, ModelRole.Champion);
        f.Add(8, ModelRole.Challenger, 'b');

        var result = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(8, ModelRole.Champion, "better", "cli:xd"), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(MlSafetyErrors.PromotionRefusedCode);
        f.Registry.Models[7].Role.ShouldBe(ModelRole.Champion);
        f.Registry.Models[8].Role.ShouldBe(ModelRole.Challenger);
        f.Registry.Designations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Naming_the_champion_that_is_champion_already_is_not_a_promotion_and_changes_nothing()
    {
        var f = new MlRollbackFixture();
        f.Add(7, ModelRole.Champion);

        var result = await Handler(f).HandleAsync(new OwnerDesignateModelCommand(7, ModelRole.Champion, "again", "cli:xd"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Changed.ShouldBeFalse();
    }
}
