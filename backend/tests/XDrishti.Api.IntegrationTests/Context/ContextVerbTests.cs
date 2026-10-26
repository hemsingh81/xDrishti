using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Context;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Context;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Context;

/// <summary>The <c>context</c> verb (P3-01d): only the first argument selects it, the command line is strict, and the two actions print what the handlers return. No database: the host is built with the handlers replaced.</summary>
public sealed class ContextVerbTests
{
    private static readonly Dictionary<string, string?> NoDatabase = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=1",
        ["BrokerVault:Key"] = string.Empty,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string[], string> Dispatch => new()
    {
        { ["context", "refresh"], "Context" },
        { ["context", "migrate"], "Context" },
        { ["context", "--healthcheck"], "Context" },
        { ["context"], "Context" },
        { ["x", "context"], "Serve" },
        { ["Context"], "Serve" },
        { ["simulate", "--basket", "context"], "Simulate" },
        { ["cost-sheet", "context"], "CostSheet" },
        { ["migrate"], "Migrate" },
    };

    [Theory]
    [MemberData(nameof(Dispatch))]
    public void Only_the_first_argument_selects_the_context_verb(string[] args, string expected)
    {
        VerbDispatch.Of(args).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("refresh", "Refresh")]
    [InlineData("coverage", "Coverage")]
    public void The_two_actions_parse(string action, string expected)
    {
        var (options, errors) = ContextOptions.Parse([action]);

        options!.Action.ToString().ShouldBe(expected);
        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData]
    [InlineData("refresh", "--now")]
    [InlineData("coverage", "refresh")]
    [InlineData("rebuild")]
    [InlineData("Refresh")]
    public void Anything_else_is_a_usage_error(params string[] args)
    {
        var (options, errors) = ContextOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldHaveSingleItem();
    }

    private sealed class FixedRefresh(ContextRefreshed answer) : ICommandHandler<RefreshContextCommand, ContextRefreshed>
    {
        public ValueTask<Result<ContextRefreshed>> HandleAsync(RefreshContextCommand command, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success(answer));
    }

    private sealed class FixedCoverage(ContextCoverageView answer) : IQueryHandler<GetContextCoverageQuery, ContextCoverageView>
    {
        public ValueTask<ContextCoverageView> HandleAsync(GetContextCoverageQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(answer);
    }

    private sealed class FixedRebuild(Result<BreadthRebuilt> answer) : ICommandHandler<RebuildBreadthCommand, BreadthRebuilt>
    {
        public RebuildBreadthCommand? Seen { get; private set; }

        public ValueTask<Result<BreadthRebuilt>> HandleAsync(RebuildBreadthCommand command, CancellationToken cancellationToken)
        {
            Seen = command;
            return ValueTask.FromResult(answer);
        }
    }

    private static ContextCoverageView View(string? missing = null) => new(
        true, missing, missing is null ? null : "Track NIFTY 50 on the Instruments screen and run Update data.", "NSE:NIFTY", "NSE:INDIAVIX", 3, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 9), 0, false,
        null, null, null, null, 3, 0, null, false, 0, 1, [new ContextSkippedView(new DateOnly(2026, 10, 6), "index_thin", 300)], "derived from 1-minute bars; the official daily close can differ slightly",
        new ContextBreadthView(null, null, null, null, null, null, 0, 3, 0, [], "breadth of your tracked shares (members as of today), not of the market"));

    private static async Task<(int Exit, string Out, string Error)> RunAsync(string[] args, ContextRefreshed? refreshed = null, ContextCoverageView? coverage = null, FixedRebuild? rebuild = null)
    {
        using var host = SimulateHost.Create(NoDatabase, services =>
        {
            services.AddScoped<ICommandHandler<RefreshContextCommand, ContextRefreshed>>(_ => new FixedRefresh(refreshed ?? new ContextRefreshed(0, 0, 0, null, null, null)));
            services.AddScoped<IQueryHandler<GetContextCoverageQuery, ContextCoverageView>>(_ => new FixedCoverage(coverage ?? View()));
            services.AddScoped<ICommandHandler<RebuildBreadthCommand, BreadthRebuilt>>(_ => rebuild ?? new FixedRebuild(Result.Success(new BreadthRebuilt(0, false, [], null))));
        }, "xd-context");
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await ContextCommand.RunAsync(args, host.Services, new SimulateConsole(output, error), Ct);

        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Refresh_prints_the_counts_and_the_last_day_and_exits_0()
    {
        var (exit, output, error) = await RunAsync(["refresh"], new ContextRefreshed(4, 2, 1, new DateOnly(2026, 10, 9), null, null));

        exit.ShouldBe(0);
        output.ShouldContain("written: 4");
        output.ShouldContain("waiting for the VIX: 2");
        output.ShouldContain("skipped: 1");
        output.ShouldContain("last day: 2026-10-09");
        error.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_prerequisite_is_printed_with_its_fix_and_is_still_exit_0()
    {
        var (exit, output, _) = await RunAsync(["refresh"], new ContextRefreshed(0, 0, 0, null, "index_not_tracked", "Track NIFTY 50 on the Instruments screen and run Update data."));

        exit.ShouldBe(0);
        output.ShouldContain("missing: index_not_tracked - Track NIFTY 50 on the Instruments screen");
    }

    [Fact]
    public async Task Coverage_prints_the_days_the_readable_dates_the_skipped_days_and_the_source_sentence()
    {
        var (exit, output, _) = await RunAsync(["coverage"]);

        exit.ShouldBe(0);
        output.ShouldContain("days: 3");
        output.ShouldContain("first day: 2026-10-07");
        output.ShouldContain("index_trend readable from: n/a");
        output.ShouldContain("  2026-10-06 index_thin 300 bars");
        output.ShouldContain("derived from 1-minute bars; the official daily close can differ slightly");
        output.ShouldContain("breadth: none yet");
        output.ShouldContain("days waiting for breadth: 3");
        output.ShouldContain("breadth of your tracked shares (members as of today), not of the market");
    }

    [Fact]
    public void Rebuild_breadth_needs_both_dates_and_takes_write_as_its_only_flag()
    {
        var (ok, _) = ContextOptions.Parse(["rebuild-breadth", "--from", "2026-01-02", "--to", "2026-03-31", "--write"]);
        var (dry, _) = ContextOptions.Parse(["rebuild-breadth", "--to", "2026-03-31", "--from", "2026-01-02"]);

        (ok!.Action, ok.From, ok.To, ok.Write).ShouldBe((ContextAction.RebuildBreadth, new DateOnly(2026, 1, 2), new DateOnly(2026, 3, 31), true));
        dry!.Write.ShouldBeFalse("dry run by default");
        foreach (var bad in new[] { new[] { "rebuild-breadth" }, ["rebuild-breadth", "--from", "2026-01-02"], ["rebuild-breadth", "--from", "yesterday", "--to", "2026-03-31"], ["rebuild-breadth", "--from", "2026-01-02", "--to", "2026-03-31", "--force"], ["rebuild-breadth", "--from"] })
        {
            ContextOptions.Parse(bad).Options.ShouldBeNull(string.Join(' ', bad));
        }
    }

    [Fact]
    public async Task Rebuild_breadth_prints_each_changed_day_with_the_old_and_the_new_figure_and_passes_the_range_and_the_write_flag()
    {
        var rebuild = new FixedRebuild(Result.Success(new BreadthRebuilt(3, false, [new BreadthChange(new DateOnly(2026, 3, 4), 40.0, "value", 60, 55.5, "value", 61, 214), new BreadthChange(new DateOnly(2026, 3, 5), null, null, null, null, "insufficient", 12, 214)], null, [new DateOnly(2026, 3, 6)])));

        var (exit, output, _) = await RunAsync(["rebuild-breadth", "--from", "2026-03-04", "--to", "2026-03-06"], rebuild: rebuild);

        exit.ShouldBe(0);
        rebuild.Seen.ShouldBe(new RebuildBreadthCommand(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6), false));
        output.ShouldContain("write: no (dry run; add --write to store)");
        output.ShouldContain("days that would change: 2");
        output.ShouldContain("  2026-03-04 old 40.0 (60 voters) new 55.5 (61 voters)");
        output.ShouldContain("  2026-03-05 old none new insufficient (12 voters)");
        output.ShouldContain("warning: 2026-03-06 can still gain bars");
        output.ShouldContain("not of the market");
    }

    [Fact]
    public async Task A_refused_rebuild_exits_1_with_the_reason()
    {
        var rebuild = new FixedRebuild(Result.Failure<BreadthRebuilt>(Error.Validation("breadth.range_too_wide", "The range may span at most 400 sessions.")));

        var (exit, output, error) = await RunAsync(["rebuild-breadth", "--from", "2020-01-01", "--to", "2026-03-06", "--write"], rebuild: rebuild);

        exit.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("breadth.range_too_wide");
    }

    [Fact]
    public async Task A_wrong_command_line_prints_the_usage_and_exits_2()
    {
        var (exit, output, error) = await RunAsync(["wipe"]);

        exit.ShouldBe(2);
        output.ShouldBeEmpty();
        error.ShouldContain("usage:");
    }

    private static string Script()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "scripts", "context.sh")))
        {
            folder = folder.Parent;
        }

        return File.ReadAllText(Path.Combine(folder.ShouldNotBeNull().FullName, "scripts", "context.sh"));
    }

    [Fact]
    public void The_script_never_traces_never_echoes_the_password_and_reads_no_vault_key_or_owner_secret()
    {
        var script = Script();

        script.ShouldNotContain("set -x");
        script.Split('\n').Where(l => l.TrimStart().StartsWith("echo", StringComparison.Ordinal)).ShouldAllBe(l => !l.Contains("$password", StringComparison.Ordinal) && !l.Contains("Database__Password", StringComparison.Ordinal));
        script.ShouldNotContain("BrokerVault");
        script.ShouldNotContain("xdrishti/broker");
        script.ShouldNotContain("xdrishti/owner");
        script.ShouldContain("xdrishti/db/app");
        script.ShouldContain("set -euo pipefail");
        script.ShouldContain("-- context \"$@\"");
    }
}
