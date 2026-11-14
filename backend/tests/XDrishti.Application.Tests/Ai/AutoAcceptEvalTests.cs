using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Application.Tests.LearnedSettings;

namespace XDrishti.Application.Tests.Ai;

/// <summary>ADR 0292 (Q1): the eval baseline is re-accepted automatically only when the setting is on and no set regressed, recorded as <c>auto:xd</c> with the setting snapshot; anything else is left to the owner.</summary>
public sealed class AutoAcceptEvalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunEvalCommand All(int repeats = 3) => new(["all"], repeats, RunEvalCommand.Builtin, EvalErrors.CliActor);

    [Fact]
    public async Task With_no_setting_row_a_gating_pass_is_accepted_as_auto_with_the_default_named()
    {
        var rig = new EvalRig();
        var run = (await rig.Run().HandleAsync(All(), Ct)).Value;

        var outcome = await rig.AutoAccept(new InMemoryEssentialStore()).HandleAsync(new AutoAcceptEvalCommand(run.Id), Ct);

        outcome.Value.State.ShouldBe("accepted");
        rig.Store.Baselines.ShouldHaveSingleItem().ShouldBe((run.Id, "auto:xd", "auto: ai.eval.auto_accept=true (default, ADR 0292)"));
    }

    [Fact]
    public async Task With_the_setting_off_nothing_is_accepted()
    {
        var rig = new EvalRig();
        var run = (await rig.Run().HandleAsync(All(), Ct)).Value;
        var settings = new InMemoryEssentialStore();
        await settings.SetAsync(AutoAcceptEvalHandler.Key, "false", EvalErrors.CliActor, DateTimeOffset.UnixEpoch, Ct);

        var outcome = await rig.AutoAccept(settings).HandleAsync(new AutoAcceptEvalCommand(run.Id), Ct);

        outcome.Value.State.ShouldBe("off");
        rig.Store.Baselines.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_setting_never_accepts()
    {
        var rig = new EvalRig();
        var run = (await rig.Run().HandleAsync(All(), Ct)).Value;
        var settings = Substitute.For<IEssentialSettingStore>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsyncForAnyArgs(new InvalidOperationException("down"));

        var outcome = await rig.AutoAccept(settings).HandleAsync(new AutoAcceptEvalCommand(run.Id), Ct);

        outcome.Value.State.ShouldBe("unavailable");
        rig.Store.Baselines.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_on_which_a_set_got_worse_than_the_baseline_is_left_to_the_owner()
    {
        var rig = new EvalRig();
        var first = (await rig.Run().HandleAsync(All(), Ct)).Value;
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(first.Id, null, EvalErrors.CliActor), Ct)).IsSuccess.ShouldBeTrue();
        rig.Answers.Override = (c, _) => c.Id == "DQ-01" ? "I do not know." : null;
        var second = (await rig.Run().HandleAsync(All(), Ct)).Value;

        var outcome = await rig.AutoAccept(new InMemoryEssentialStore()).HandleAsync(new AutoAcceptEvalCommand(second.Id), Ct);

        second.Verdict.ShouldBe("PASS", "one wrong answer stays above the threshold, but the set got worse");
        outcome.Value.State.ShouldBe("not_eligible");
        rig.Store.Baselines.ShouldHaveSingleItem().Run.ShouldBe(first.Id);
    }

    [Fact]
    public async Task A_look_only_run_is_never_accepted()
    {
        var rig = new EvalRig();
        var run = (await rig.Run().HandleAsync(All(1), Ct)).Value;

        var outcome = await rig.AutoAccept(new InMemoryEssentialStore()).HandleAsync(new AutoAcceptEvalCommand(run.Id), Ct);

        outcome.Value.State.ShouldBe("not_eligible");
        rig.Store.Baselines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_owners_verb_changes_the_setting()
    {
        var settings = new InMemoryEssentialStore();
        var handler = new SetEvalAutoAcceptHandler(settings, TimeProvider.System);

        (await handler.HandleAsync(new SetEvalAutoAcceptCommand(false, "auto:xd"), Ct)).Error.Code.ShouldBe("assistant.owner_only");
        (await handler.HandleAsync(new SetEvalAutoAcceptCommand(false, EvalErrors.CliActor), Ct)).Value.State.ShouldBe("off");

        (await settings.GetAsync(AutoAcceptEvalHandler.Key, Ct))!.Value.ShouldBe("false");
    }
}
