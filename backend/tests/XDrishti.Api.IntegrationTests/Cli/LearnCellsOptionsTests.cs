using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn cells</c> (spec P6-01): pure parsing, no database.</summary>
public sealed class LearnCellsOptionsTests
{
    private static readonly string Key = new('a', 64);

    [Fact]
    public void A_full_command_line_is_parsed_with_the_defaults_of_doc_16_for_what_is_left_out()
    {
        var (options, errors) = LearnCellsOptions.Parse(["--as-of", "2026-04-30", "--run", $"{Key}:intraday:FIXED_RR", "--run", $"{Key}:swing:ATR"]);

        errors.ShouldBeEmpty();
        options!.AsOf.ShouldBe(new DateOnly(2026, 4, 30));
        options.Sources.Count.ShouldBe(2);
        options.Sources[1].Style.ShouldBe("swing");
        options.Options.RecencyHalfLifeDays.ShouldBe(180);
        options.Options.PriorStrength.ShouldBe(20);
        options.Options.MinTrades.ShouldBe(30);
    }

    [Fact]
    public void The_knobs_can_be_set()
    {
        var (options, _) = LearnCellsOptions.Parse(["--as-of", "2026-04-30", "--run", $"{Key}:swing:X", "--half-life-days", "90", "--prior-strength", "10.5", "--min-trades", "20"]);

        options!.Options.RecencyHalfLifeDays.ShouldBe(90);
        options.Options.PriorStrength.ShouldBe(10.5);
        options.Options.MinTrades.ShouldBe(20);
    }

    [Theory]
    [InlineData("--run", "k:swing")]
    [InlineData("--as-of", "30/04/2026")]
    [InlineData("--bogus", "1")]
    [InlineData("--half-life-days", "many")]
    [InlineData("--min-trades", "2.5")]
    [InlineData("--min-trades", "x")]
    public void Wrong_arguments_are_listed_and_give_no_options(string name, string value)
    {
        var (options, errors) = LearnCellsOptions.Parse(name == "--as-of" ? [name, value, "--run", $"{Key}:swing:X"] : ["--as-of", "2026-04-30", name, value]);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_date_a_missing_run_and_a_repeated_option_are_errors()
    {
        LearnCellsOptions.Parse([]).Errors.Count.ShouldBe(2);
        LearnCellsOptions.Parse(["--as-of", "2026-01-01", "--as-of", "2026-01-02", "--run", $"{Key}:swing:X"]).Errors.ShouldNotBeEmpty();
        LearnCellsOptions.Parse(["--as-of"]).Errors.ShouldNotBeEmpty();
        LearnCellsOptions.Parse(["--run", "a:b"]).Errors.ShouldNotBeEmpty();
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(Result<CellStatsOutcome> result, params string[] args)
    {
        var services = new ServiceCollection().AddSingleton<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>(new FixedHandler(result)).BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await XdCommand.RunAsync(["learn", "cells", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task The_verb_prints_the_outcome_and_exits_0()
    {
        var (code, output, _) = await RunAsync(Result.Success(new CellStatsOutcome(new string('c', 64), 2, 6, 40, false)), "--as-of", "2026-04-30", "--run", $"{Key}:swing:X");

        code.ShouldBe(0);
        output.ShouldContain("2 cells, 6 rows from 40 trades");
        output.ShouldContain("cccccccccccc (stored)");
    }

    [Fact]
    public async Task A_refusal_exits_1_with_the_code_and_a_bad_line_exits_2()
    {
        var refused = await RunAsync(Result.Failure<CellStatsOutcome>(LearningErrors.RunNotFound(Key)), "--as-of", "2026-04-30", "--run", $"{Key}:swing:X");
        var usage = await RunAsync(Result.Success(new CellStatsOutcome(Key, 0, 0, 0, false)), "--as-of", "nope");

        refused.Code.ShouldBe(1);
        refused.Err.ShouldContain("learning.run_not_found");
        usage.Code.ShouldBe(2);
        usage.Err.ShouldContain("usage:");
    }

    private sealed class FixedHandler(Result<CellStatsOutcome> result) : ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>
    {
        public ValueTask<Result<CellStatsOutcome>> HandleAsync(ComputeCellStatsCommand command, CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }
}
