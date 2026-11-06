using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verb <c>xd learn influence</c> (spec CF-10, part 10d-2a) over fake use cases: the options, the text with every term state, the unavailable path, the switches, the refusals and the routing through <see cref="XdCommand"/>.</summary>
public sealed class LearnInfluenceCommandTests
{
    private sealed class FakeInfluence(Func<GetMlInfluenceQuery, Result<MlInfluenceView>> answer) : IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>
    {
        public List<GetMlInfluenceQuery> Seen { get; } = [];

        public ValueTask<Result<MlInfluenceView>> HandleAsync(GetMlInfluenceQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer(query));
        }
    }

    private sealed class FakeSwitches(Result<RollbackProposalsView> answer) : IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>
    {
        public List<GetRollbackProposalsQuery> Seen { get; } = [];

        public ValueTask<Result<RollbackProposalsView>> HandleAsync(GetRollbackProposalsQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer);
        }
    }

    private static MlInfluenceView Refused(long id = 5) => new(
        id, "challenger", false,
        [
            new MlTermView("HoldOutBeatsNoMl", "unavailable", [new MlReasonView("holdout_unavailable", "The hold-out rows are not scored for this model.")]),
            new MlTermView("ForwardShadowOk", "fails", [new MlReasonView("forward_short", "Only 12 graded candidates; 30 are needed.")]),
            new MlTermView("NoCalibrationAlarm", "holds", []),
            new MlTermView("SwitchOn", "fails", [new MlReasonView("switch_off", "ml.influence.enabled is OFF.")]),
        ],
        [new MlReasonView("holdout_unavailable", "The hold-out rows are not scored for this model."), new MlReasonView("not_champion", "The model is not the champion.")],
        new MlHoldOutView("kept", "model-family:v3", null, null, null, null),
        false, new string('e', 64), "min_rows=200", ["An unavailable figure is never a pass."]);

    private static MlInfluenceView Allowed() => Refused() with
    {
        Role = "champion",
        MayInfluence = true,
        Terms = [new MlTermView("HoldOutBeatsNoMl", "holds", []), new MlTermView("SwitchOn", "holds", [])],
        Reasons = [],
        HoldOut = new MlHoldOutView("used", "model-family:v3", new DateOnly(2026, 9, 1), 240, 0.97, 0.0312),
        SwitchOn = true,
    };

    private static Result<RollbackProposalsView> Switches(bool influence = false, bool promote = false, bool rollback = false) =>
        Result.Success(new RollbackProposalsView([], new MlSettingsView(influence, influence ? 3 : null, promote, null, rollback, rollback ? 4 : null), ["A proposal changes nothing by itself."]));

    // the text is wrapped to a terminal width, so a sentence is compared with its line breaks and indents folded to one space
    private static async Task<(int Code, string Out, string Error)> RunAsync(FakeInfluence influence, FakeSwitches switches, params string[] args)
    {
        var (code, output, error) = await RunRawAsync(influence, switches, args);

        return (code, string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), error);
    }

    private static async Task<(int Code, string Out, string Error)> RunRawAsync(FakeInfluence influence, FakeSwitches switches, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>>(influence)
            .AddSingleton<IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>>(switches)
            .BuildServiceProvider();

        var code = await XdCommand.RunAsync(["learn", "influence", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void The_model_id_is_one_positive_whole_number()
    {
        var (options, errors) = LearnInfluenceOptions.Parse([" 12 "]);

        errors.ShouldBeEmpty();
        options!.ModelId.ShouldBe(12);
    }

    [Theory]
    [InlineData]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1", "2")]
    [InlineData("1", "--all")]
    public void Wrong_input_lists_errors_and_no_options(params string[] args)
    {
        var (options, errors) = LearnInfluenceOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_refused_model_reads_not_a_pass_with_every_reason_the_switches_and_the_unavailable_figures()
    {
        var influence = new FakeInfluence(_ => Result.Success(Refused()));
        var switches = new FakeSwitches(Switches());

        var (code, output, error) = await RunAsync(influence, switches, "5");

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldContain("influence of model #5: role challenger, champion: no");
        output.ShouldContain("verdict: NOT a pass.");
        output.ShouldContain("HoldOutBeatsNoMl: UNAVAILABLE (not a pass) holdout_unavailable: The hold-out rows are not scored for this model.");
        output.ShouldContain("ForwardShadowOk: FAILS (not a pass) forward_short: Only 12 graded candidates; 30 are needed.");
        output.ShouldContain("NoCalibrationAlarm: HOLDS");
        output.ShouldContain("not_champion: The model is not the champion.");
        output.ShouldContain("hold-out: kept (family model-family:v3, block start none)");
        output.ShouldContain("odds: unavailable: there is no hold-out score for this model");
        output.ShouldContain("interval: unavailable: this version has no source for an interval of the hold-out probability. Unavailable is not a finding and not a pass.");
        output.ShouldContain("effect of the take rule on realised R: unavailable (not a pass)");
        output.ShouldContain("ml.influence.enabled: OFF (no model may influence a decision)");
        output.ShouldContain("ml.auto_promote: OFF (promotion is the owner's act)");
        output.ShouldContain("ml.auto_rollback: OFF (a rollback proposal waits for the owner)");
        output.ShouldContain("An unavailable figure is never a pass.");
        influence.Seen.ShouldHaveSingleItem().Id.ShouldBe(5);
        switches.Seen.ShouldHaveSingleItem().ModelId.ShouldBe(5);
    }

    [Fact]
    public async Task An_allowed_champion_shows_its_numbers_and_the_switches_that_are_on()
    {
        var (code, output, _) = await RunAsync(new FakeInfluence(_ => Result.Success(Allowed())), new FakeSwitches(Switches(influence: true, promote: true, rollback: true)), "5");

        code.ShouldBe(0);
        output.ShouldContain("role champion, champion: yes");
        output.ShouldContain("every term holds and the model is the champion");
        output.ShouldContain("reasons it may not influence: none");
        output.ShouldContain("hold-out: used (family model-family:v3, block start 2026-09-01)");
        output.ShouldContain("odds: probability of beating the best no-ML baseline 0.9700 on 240 row(s)");
        output.ShouldContain("effect of the take rule on realised R: 0.0312");
        output.ShouldContain("ml.influence.enabled: ON");
        output.ShouldContain("ml.auto_promote: ON");
        output.ShouldContain("ml.auto_rollback: ON");
        output.ShouldNotContain("verdict: NOT a pass");
        output.ShouldNotContain("odds: unavailable");
    }

    [Fact]
    public async Task An_unknown_model_exits_1_with_the_reason_and_asks_for_nothing_else()
    {
        var influence = new FakeInfluence(_ => Result.Failure<MlInfluenceView>(Error.NotFound("models.not_found", "No model 99.")));
        var switches = new FakeSwitches(Switches());

        var (code, output, error) = await RunAsync(influence, switches, "99");

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("models.not_found: No model 99.");
        switches.Seen.ShouldBeEmpty();
    }

    [Theory]
    [InlineData]
    [InlineData("x")]
    [InlineData("5", "6")]
    public async Task A_bad_line_exits_2_without_calling_a_use_case(params string[] args)
    {
        var influence = new FakeInfluence(_ => Result.Success(Refused()));
        var switches = new FakeSwitches(Switches());

        var (code, _, error) = await RunAsync(influence, switches, args);

        code.ShouldBe(2);
        error.ShouldContain("usage:");
        influence.Seen.ShouldBeEmpty();
        switches.Seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_switch_read_is_shown_as_unavailable_not_as_off()
    {
        var failing = new FakeSwitches(Result.Failure<RollbackProposalsView>(Error.Validation("learning.invalid", "no")));

        var (code, output, _) = await RunAsync(new FakeInfluence(_ => Result.Success(Refused())), failing, "5");

        code.ShouldBe(0);
        output.ShouldContain("ML switches: unavailable: learning.invalid: no. Unavailable is not a finding and not a pass.");
        output.ShouldNotContain("ml.influence.enabled: OFF");
        output.ShouldNotContain("ml.auto_rollback:");
    }

    [Fact]
    public async Task A_use_case_that_throws_exits_3_and_a_cancelled_run_exits_130()
    {
        var throwing = new FakeInfluence(_ => throw new InvalidOperationException("db down"));
        var (failed, _, failure) = await RunAsync(throwing, new FakeSwitches(Switches()), "5");

        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var cancelling = new FakeInfluence(_ => throw new OperationCanceledException(source.Token));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection()
            .AddSingleton<IQueryHandler<GetMlInfluenceQuery, Result<MlInfluenceView>>>(cancelling)
            .AddSingleton<IQueryHandler<GetRollbackProposalsQuery, Result<RollbackProposalsView>>>(new FakeSwitches(Switches()))
            .BuildServiceProvider();
        var cancelled = await XdCommand.RunAsync(["learn", "influence", "5"], services, new SimulateConsole(output, error), TimeSpan.Zero, source.Token);

        failed.ShouldBe(3);
        failure.ShouldContain("failed:");
        cancelled.ShouldBe(130);
    }

    [Fact]
    public async Task Long_sentences_are_wrapped_and_stored_text_cannot_write_control_characters()
    {
        var view = Refused() with
        {
            Notes = [string.Join(' ', Enumerable.Repeat("word", 80)), "bad\u001b[31m text\nsecond line"],
            Terms = [new MlTermView("Bad\u001bTerm", "fails", [new MlReasonView("c\u001bd", "reason\u0007 text")])],
        };

        var (_, output, _) = await RunRawAsync(new FakeInfluence(_ => Result.Success(view)), new FakeSwitches(Switches()), "5");

        output.Split('\n').ShouldAllBe(l => l.TrimEnd('\r').Length <= LearnReportText.Width);
        output.ShouldNotContain("\u001b");
        output.ShouldNotContain("\u0007");
        string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)).ShouldContain("bad [31m text second line");
    }
}
