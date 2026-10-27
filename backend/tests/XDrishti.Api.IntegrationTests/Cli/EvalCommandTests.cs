using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>Spec P9-09 Contract: the command line of <c>xd eval</c>, the exit codes (0 only for a gating PASS), the owner's actor, and that the verb sets no switch and takes no password.</summary>
public sealed class EvalCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void The_run_verb_defaults_to_all_sets_three_repeats_and_the_builtin_target()
    {
        var (options, errors) = EvalCliOptions.Parse(["run"]);

        errors.ShouldBeEmpty();
        (options!.Verb, options.Repeats, options.Target, options.Format).ShouldBe(("run", 3, "builtin", "text"));
        options.Sets.ShouldBe(["all"]);
        EvalCliOptions.Parse(["run", "--set", "data", "--set", "plans", "--repeats", "1"]).Options!.Sets.ShouldBe(["data", "plans"]);
        EvalCliOptions.Parse(["grade", "--answers", "a.jsonl"]).Options!.Target.ShouldBe("hermes");
        EvalCliOptions.Parse(["show", RunId.ToString(), "--md"]).Options!.Format.ShouldBe("md");
        EvalCliOptions.Parse(["compare", RunId.ToString(), "--baseline", Guid.Empty.ToString()]).Options!.Baseline.ShouldBe(Guid.Empty);
        EvalCliOptions.Parse(["accept", RunId.ToString(), "--reason", "smaller model"]).Options!.Reason.ShouldBe("smaller model");
    }

    [Theory]
    [InlineData]
    [InlineData("jump")]
    [InlineData("run", "--repeats", "x")]
    [InlineData("run", "--bogus")]
    [InlineData("grade")]
    [InlineData("compare")]
    [InlineData("accept", "not-a-guid")]
    [InlineData("show", "--md")]
    [InlineData("run", "--password", "x")]
    [InlineData("accept", "22222222-2222-2222-2222-222222222222", "--auto")]
    public void A_wrong_command_line_is_a_usage_error(params string[] args)
    {
        var (options, errors) = EvalCliOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_names_the_owner_acts_and_offers_no_switch_password_or_auto_option()
    {
        EvalCliOptions.Usage.ShouldContain("the owner's act");
        EvalCliOptions.Usage.ShouldNotContain("--auto");
        EvalCliOptions.Usage.ShouldNotContain("--password");
        EvalCliOptions.Usage.ShouldContain("it is never set here");
    }

    private static EvalRunView View(string verdict, bool gating, string status = "completed") =>
        new(RunId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "builtin", "main", ["data"], 3, gating, "m", new string('a', 64), "b1", new string('f', 64), status, verdict, "cli:xd", false,
            new RunSummary(verdict, !gating, [new SetSummary("data", SetOutcome.Of("data", verdict == "PASS" ? SetState.Pass : SetState.Fail, verdict == "PASS" ? [] : ["data pass 20 of 30 is below 27"]), [RateLine.Of("pass", 28, 30)], 30, 3, 0.9, null, null)], "30 cases cannot separate 27 from 24 correct"));

    private sealed class Handlers(Result<EvalRunView> result) : ICommandHandler<RunEvalCommand, EvalRunView>, ICommandHandler<AcceptEvalCommand, EvalRunView>
    {
        public RunEvalCommand? Sent { get; private set; }

        public ValueTask<Result<EvalRunView>> HandleAsync(RunEvalCommand command, CancellationToken cancellationToken)
        {
            Sent = command;
            return ValueTask.FromResult(result);
        }

        public ValueTask<Result<EvalRunView>> HandleAsync(AcceptEvalCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(command.Actor == "cli:xd" ? Result.Success(View("PASS", true)) : Result.Failure<EvalRunView>(Error.Forbidden("assistant.owner_only", "no")));
    }

    private static async Task<(int Code, string Out, string Error, RunEvalCommand? Sent)> RunAsync(string[] args, Result<EvalRunView> result)
    {
        var handlers = new Handlers(result);
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<RunEvalCommand, EvalRunView>>(handlers);
        services.AddSingleton<ICommandHandler<AcceptEvalCommand, EvalRunView>>(handlers);
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await EvalCommand.RunAsync(args, provider, new SimulateConsole(output, error), Ct);

        return (code, output.ToString(), error.ToString(), handlers.Sent);
    }

    [Fact]
    public async Task A_gating_pass_exits_zero_prints_the_rates_with_intervals_and_the_owners_actor_is_sent()
    {
        var (code, output, _, sent) = await RunAsync(["run", "--set", "data", "--repeats", "3"], Result.Success(View("PASS", true)));

        code.ShouldBe(0);
        (sent!.Repeats, sent.Target, sent.Actor).ShouldBe((3, "builtin", "cli:xd"));
        sent.Sets.ShouldBe(["data"]);
        output.ShouldContain("verdict PASS");
        output.ShouldContain("pass: 28 of 30 (95 % interval");
        output.ShouldContain("30 cases cannot separate 27 from 24 correct");
    }

    [Fact]
    public async Task A_failing_run_exits_four_a_non_gating_pass_exits_four_and_a_refusal_exits_one()
    {
        (await RunAsync(["run"], Result.Success(View("FAIL", true)))).Code.ShouldBe(4);
        var nonGating = await RunAsync(["run", "--repeats", "1"], Result.Success(View("PASS", false)));
        nonGating.Code.ShouldBe(4);
        nonGating.Out.ShouldContain("non-gating: it can never be accepted");
        (await RunAsync(["run"], Result.Success(View("INCOMPLETE", false, "model_unavailable")))).Code.ShouldBe(1);
        var refused = await RunAsync(["run"], Result.Failure<EvalRunView>(new Error("eval.wrong_database", "not the fixture", ErrorKind.Conflict)));
        refused.Code.ShouldBe(1);
        refused.Error.ShouldContain("eval.wrong_database");
        (await RunAsync(["run", "--bogus"], Result.Success(View("PASS", true)))).Code.ShouldBe(2);
    }

    [Fact]
    public async Task Accept_is_made_as_the_owners_verb()
    {
        var (code, output, _, _) = await RunAsync(["accept", RunId.ToString(), "--reason", "ok"], Result.Success(View("PASS", true)));

        code.ShouldBe(0);
        output.ShouldContain("is the baseline");
    }

    [Fact]
    public async Task Seeding_needs_the_connection_from_the_environment_and_nothing_is_seeded_without_it()
    {
        Environment.SetEnvironmentVariable("Eval__SeedConnection", null);

        var (code, _, error, _) = await RunAsync(["seed"], Result.Success(View("PASS", true)));

        code.ShouldBe(2);
        error.ShouldContain("Eval__SeedConnection");
    }
}
