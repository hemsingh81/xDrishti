using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>P5-07: the dispatch, the command lines, and the verb against fake handlers (no database, no host start).</summary>
public sealed class XdCommandTests : IDisposable
{
    private static readonly Guid RunId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _dir = Directory.CreateTempSubdirectory("xd-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData("xd", "Xd")]
    [InlineData("Xd", "Serve")]
    public void Only_the_first_argument_selects_the_xd_verb(string first, string expected) => VerbDispatch.Of([first, "strategy", "test"]).ToString().ShouldBe(expected);

    [Fact]
    public void A_symbol_list_or_file_named_xd_never_selects_the_verb() => VerbDispatch.Of(["simulate", "--symbols", "xd"]).ToString().ShouldBe("Simulate");

    [Theory]
    [InlineData("strategy validate a.yaml")]
    [InlineData("strategy test a.yaml --symbols RELIANCE,TCS --method full --from 2025-01-01 --to 2025-06-30 --timeout-minutes 5")]
    [InlineData("strategy test a.yaml --basket-id 11111111-1111-1111-1111-111111111111")]
    [InlineData("lab runs --strategy ORB --state succeeded --limit 10")]
    public void A_valid_command_line_parses(string line) => XdOptions.Parse(line.Split(' ')).Options.ShouldNotBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("strategy")]
    [InlineData("strategy run a.yaml")]
    [InlineData("strategy validate")]
    [InlineData("strategy validate a.yaml --symbols X")]
    [InlineData("strategy test a.yaml")]
    [InlineData("strategy test a.yaml --symbols A --basket-id 11111111-1111-1111-1111-111111111111")]
    [InlineData("strategy test a.yaml --symbols A --method slow")]
    [InlineData("strategy test a.yaml --symbols A --from 2025-01-01")]
    [InlineData("strategy test a.yaml --symbols A --timeout-minutes 0")]
    [InlineData("strategy test a.yaml --symbols A --bogus 1")]
    [InlineData("lab runs a.yaml")]
    [InlineData("lab runs --limit 500")]
    public void A_wrong_command_line_is_refused_with_errors(string line)
    {
        var parsed = XdOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Validate_prints_located_problems_on_stderr_and_exits_1()
    {
        var fake = new Fake { Validation = new StrategyValidation(false, null, null, null, null, [], [], [new StrategyIssue("entry.when", "rule.unknown", "unknown name", "error", 7, 3, null, null, null, null, "close")]) };

        var (code, output, error) = await RunAsync(fake, "strategy validate", File("a.yaml"));

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("a.yaml:7:3: [entry.when] error rule.unknown unknown name (try: close)");
    }

    [Fact]
    public async Task Validate_of_a_good_file_prints_the_hash_and_exits_0()
    {
        var (code, output, _) = await RunAsync(new Fake(), "strategy validate", File("a.yaml"));

        code.ShouldBe(0);
        output.ShouldContain("hash abc");
    }

    [Fact]
    public async Task A_missing_file_is_refused_before_anything_is_called()
    {
        var fake = new Fake();

        var (code, _, error) = await RunAsync(fake, "strategy validate", Path.Combine(_dir, "none.yaml"));

        code.ShouldBe(1);
        error.ShouldContain("cannot be read");
        fake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Test_stores_the_version_queues_the_run_waits_and_prints_the_result()
    {
        var fake = new Fake { Reads = [Run("running"), Run("succeeded")] };

        var (code, output, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "RELIANCE,TCS");

        code.ShouldBe(0);
        fake.Calls.ShouldBe(["create", "start", "get", "get"]);
        fake.Started!.Symbols.ShouldBe(["RELIANCE", "TCS"]);
        fake.Started.Method.ShouldBe("quick");
        error.ShouldContain("stored ORB version 2 (new)");
        output.ShouldContain("run " + RunId.ToString("D") + " succeeded");
        output.ShouldContain("trades 9");
    }

    [Theory]
    [InlineData("PASS", 0)]
    [InlineData("MARGINAL", 4)]
    [InlineData("FAIL", 4)]
    public async Task A_full_test_exits_by_its_verdict(string outcome, int expected)
    {
        var verdict = new LabVerdictView(outcome, 1, 1.0, new EvidenceView(100, 0.2, 1.4, 6, 5, 3, 1, "walk_forward"), [new GateView("trades", 100, 100, 100, true, "enough")], [], [], [], []);
        var fake = new Fake { Reads = [Run("succeeded", verdict)] };

        var (code, output, _) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS", "--method", "full");

        code.ShouldBe(expected);
        output.ShouldContain("verdict " + outcome);
        output.ShouldContain("pass trades: enough");
    }

    [Fact]
    public async Task A_run_that_fails_with_located_problems_prints_them_and_exits_1()
    {
        var problem = new LabProblem("compile", "exit.trail", "rule.bad", "no such trail", "error", 4, 2);
        var fake = new Fake { Reads = [Run("failed") with { Problems = [problem], Error = "compile failed" }] };

        var (code, _, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS");

        code.ShouldBe(1);
        error.ShouldContain("compile: 4:2: [exit.trail] error rule.bad no such trail");
    }

    [Fact]
    public async Task A_refused_run_is_exit_1_and_an_interrupted_run_is_exit_3()
    {
        var refused = new Fake { StartError = LabErrors.NoData(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1)) };
        (await RunAsync(refused, "strategy test", File("a.yaml"), "--symbols", "TCS")).Code.ShouldBe(1);

        var interrupted = new Fake { Reads = [Run("interrupted")] };
        (await RunAsync(interrupted, "strategy test", File("a.yaml"), "--symbols", "TCS")).Code.ShouldBe(3);
    }

    [Fact]
    public async Task Ctrl_C_while_waiting_cancels_the_run_and_exits_130()
    {
        using var cancel = new CancellationTokenSource();
        var fake = new Fake { Reads = [Run("running")], OnGet = cancel.Cancel };

        var (code, _, error) = await RunAsync(fake, cancel.Token, "strategy test", File("a.yaml"), "--symbols", "TCS");

        code.ShouldBe(130);
        fake.Calls.ShouldContain("cancel");
        error.ShouldContain("asked to stop");
    }

    [Fact]
    public async Task Ctrl_C_right_after_the_run_is_queued_still_cancels_it()
    {
        using var cancel = new CancellationTokenSource();
        var fake = new Fake { Reads = [Run("running")], OnStart = cancel.Cancel };

        var (code, _, _) = await RunAsync(fake, cancel.Token, "strategy test", File("a.yaml"), "--symbols", "TCS");

        code.ShouldBe(130);
        fake.Calls.ShouldContain("cancel");
    }

    [Fact]
    public async Task A_failed_cancel_is_said_and_the_exit_is_still_130()
    {
        using var cancel = new CancellationTokenSource();
        var fake = new Fake { Reads = [Run("running")], OnGet = cancel.Cancel, CancelFails = true };

        var (code, _, error) = await RunAsync(fake, cancel.Token, "strategy test", File("a.yaml"), "--symbols", "TCS");

        code.ShouldBe(130);
        error.ShouldContain("could not cancel run " + RunId.ToString("D"));
    }

    [Fact]
    public async Task A_run_that_outlasts_the_timeout_exits_3_and_is_left_running()
    {
        var time = new FakeTimeProvider();
        var fake = new Fake { Reads = [Run("running")], Time = time, OnGet = () => time.Advance(TimeSpan.FromMinutes(2)) };

        var (code, _, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS", "--timeout-minutes", "1");

        code.ShouldBe(3);
        error.ShouldContain("timed out after 1 minutes");
        fake.Calls.ShouldNotContain("cancel");
    }

    [Fact]
    public async Task A_refused_start_prints_its_code_and_message_and_waits_for_nothing()
    {
        var fake = new Fake { StartError = new Error("lab.no_data", "no bars\u001b[31m") };

        var (code, _, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS");

        code.ShouldBe(1);
        error.ShouldContain("lab.no_data: no bars?[31m");
        fake.Calls.ShouldBe(["create", "start"]);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_before_anything_is_called()
    {
        var path = Path.Combine(_dir, "big.yaml");
        System.IO.File.WriteAllText(path, new string('x', LabErrors.MaxTextBytes + 1));
        var fake = new Fake();

        var (code, _, error) = await RunAsync(fake, "strategy test", path, "--symbols", "TCS");

        code.ShouldBe(1);
        error.ShouldNotBeEmpty();
        fake.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("full")]
    [InlineData("robust")]
    public async Task A_succeeded_full_or_robust_run_without_a_verdict_exits_3(string method)
    {
        var fake = new Fake { Reads = [Run("succeeded") with { Method = method }] };

        var (code, _, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS", "--method", "full");

        code.ShouldBe(3);
        error.ShouldContain("without a verdict");
    }

    [Fact]
    public async Task More_problems_than_the_cap_are_counted_and_a_pathless_one_has_a_space()
    {
        var issues = Enumerable.Range(0, SimulateCommand.MaxProblems + 3).Select(_ => new StrategyIssue("p", "c", "m", "error", null, null, null, null, null, null, null)).ToList();
        var fake = new Fake { Validation = new StrategyValidation(false, null, null, null, null, [], [], issues) };

        var (_, _, error) = await RunAsync(fake, "strategy validate", File("a.yaml"));

        error.ShouldContain("a.yaml: [p] error c m");
        error.ShouldContain("3 more problems omitted");
    }

    [Fact]
    public async Task Run_derived_text_has_its_control_characters_replaced()
    {
        var fake = new Fake { Reads = [Run("failed") with { Error = "boom\u001b[2J" }] };

        var (_, _, error) = await RunAsync(fake, "strategy test", File("a.yaml"), "--symbols", "TCS");

        error.ShouldContain("error: boom?[2J");
        error.ShouldNotContain("\u001b");
    }

    [Fact]
    public async Task Runs_lists_the_runs_one_a_line()
    {
        var fake = new Fake { Listed = [Run("succeeded")] };

        var (code, output, _) = await RunAsync(fake, "lab runs");

        code.ShouldBe(0);
        output.ShouldContain("succeeded");
        output.ShouldContain("ORB v2");
    }

    private string File(string name)
    {
        var path = Path.Combine(_dir, name);
        System.IO.File.WriteAllText(path, "schema: 1\n");
        return path;
    }

    private static Task<(int Code, string Output, string Error)> RunAsync(Fake fake, params string[] words) => RunAsync(fake, CancellationToken.None, words);

    private static async Task<(int Code, string Output, string Error)> RunAsync(Fake fake, CancellationToken token, params string[] words)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fake.Time);
        services.AddSingleton<IQueryHandler<ValidateStrategyQuery, StrategyValidation>>(fake);
        services.AddSingleton<ICommandHandler<CreateStrategyCommand, CreatedStrategy>>(fake);
        services.AddSingleton<ICommandHandler<StartLabRunCommand, StartedLabRun>>(fake);
        services.AddSingleton<IQueryHandler<GetLabRunQuery, Result<LabRunView>>>(fake);
        services.AddSingleton<IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>>(fake);
        services.AddSingleton<ICommandHandler<CancelLabRunCommand, LabRunView>>(fake);
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var args = words.SelectMany(w => w.Contains(' ', StringComparison.Ordinal) && !System.IO.File.Exists(w) ? w.Split(' ') : [w]).ToArray();
        var code = await XdCommand.RunAsync(args, provider, new SimulateConsole(output, error), TimeSpan.Zero, token);
        return (code, output.ToString(), error.ToString());
    }

    private static LabRunView Run(string state, LabVerdictView? verdict = null) => new(
        RunId, "key1", state, "quick_test", new LabStrategyRef("ORB", 2, "abc"), new LabUniverse("symbols", null, "TCS", 1, [new LabInstrument(Guid.Empty, "TCS")], false, null),
        new LabWindow(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), "quick", new LabProgressView(1, 1), ["sim-1"], new LabCounts(1, 1, 0, 9, 0), [], [], ["gross"], null, false,
        new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), null, null, verdict);

    private sealed class Fake :
        IQueryHandler<ValidateStrategyQuery, StrategyValidation>,
        ICommandHandler<CreateStrategyCommand, CreatedStrategy>,
        ICommandHandler<StartLabRunCommand, StartedLabRun>,
        IQueryHandler<GetLabRunQuery, Result<LabRunView>>,
        IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>,
        ICommandHandler<CancelLabRunCommand, LabRunView>
    {
        private int _read;

        public List<string> Calls { get; } = [];

        public StrategyValidation Validation { get; init; } = new(true, "abc", "ORB", "intraday", 2, [], [], []);

        public IReadOnlyList<LabRunView> Reads { get; init; } = [Run("succeeded")];

        public IReadOnlyList<LabRunView> Listed { get; init; } = [];

        public Error? StartError { get; init; }

        public Action? OnGet { get; init; }

        public Action? OnStart { get; init; }

        public bool CancelFails { get; init; }

        public TimeProvider Time { get; init; } = TimeProvider.System;

        public StartLabRunCommand? Started { get; private set; }

        ValueTask<StrategyValidation> IQueryHandler<ValidateStrategyQuery, StrategyValidation>.HandleAsync(ValidateStrategyQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(Validation);

        ValueTask<Result<CreatedStrategy>> ICommandHandler<CreateStrategyCommand, CreatedStrategy>.HandleAsync(CreateStrategyCommand command, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            return ValueTask.FromResult(Result.Success(new CreatedStrategy("ORB", 2, "abc", null, true)));
        }

        ValueTask<Result<StartedLabRun>> ICommandHandler<StartLabRunCommand, StartedLabRun>.HandleAsync(StartLabRunCommand command, CancellationToken cancellationToken)
        {
            Calls.Add("start");
            Started = command;
            OnStart?.Invoke();
            return ValueTask.FromResult(StartError is { } e ? Result.Failure<StartedLabRun>(e) : Result.Success(new StartedLabRun(Run("queued"), true)));
        }

        ValueTask<Result<LabRunView>> IQueryHandler<GetLabRunQuery, Result<LabRunView>>.HandleAsync(GetLabRunQuery query, CancellationToken cancellationToken)
        {
            Calls.Add("get");
            OnGet?.Invoke();
            return ValueTask.FromResult(Result.Success(Reads[Math.Min(_read++, Reads.Count - 1)]));
        }

        ValueTask<Result<IReadOnlyList<LabRunView>>> IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>.HandleAsync(ListLabRunsQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Success(Listed));

        ValueTask<Result<LabRunView>> ICommandHandler<CancelLabRunCommand, LabRunView>.HandleAsync(CancelLabRunCommand command, CancellationToken cancellationToken)
        {
            Calls.Add("cancel");
            return ValueTask.FromResult(CancelFails ? Result.Failure<LabRunView>(new Error("lab.cancel", "no")) : Result.Success(Run("cancelled")));
        }
    }
}
