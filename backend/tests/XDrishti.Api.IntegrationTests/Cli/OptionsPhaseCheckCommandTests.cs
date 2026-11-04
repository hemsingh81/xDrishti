using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionPhaseCheck;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verb <c>xd options phase-check</c> (spec P14-17) over a fake use case: the exit code is 0 whatever the verdict, text or JSON comes from the same report, the cut list reaches the use case and a refusal or a bad line has its own code. Routing through <see cref="XdCommand"/> is covered too.</summary>
public sealed class OptionsPhaseCheckCommandTests
{
    private sealed class Fake(Func<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>> answer) : IQueryHandler<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>>
    {
        public List<OptionsPhaseCheckQuery> Seen { get; } = [];

        public ValueTask<Result<OptionsPhaseCheckView>> HandleAsync(OptionsPhaseCheckQuery query, CancellationToken cancellationToken)
        {
            Seen.Add(query);
            return ValueTask.FromResult(answer(query));
        }
    }

    private static Result<OptionsPhaseCheckView> Report(string verdict)
    {
        var rows = verdict == "NOT MET" ? new[] { new OptionsCellRow("trading.plan_items", 9) } : [];
        var report = OptionsPhaseCheck.Evaluate(
            new OptionsPhaseFacts([.. OptionsPhaseCheck.Families.Select(f => new OptionsFamilyFacts(f, true, []))], new OptionsCellFacts(true, rows, false), new OptionsArchiveFacts(2.1, [], false, false), new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero), 90, null),
            []);
        return Result.Success(new OptionsPhaseCheckView(report, report.ToText(), report.ToJson()));
    }

    private static async Task<(int Code, string Out, string Error)> RunAsync(Fake fake, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection().AddSingleton<IQueryHandler<OptionsPhaseCheckQuery, Result<OptionsPhaseCheckView>>>(fake).BuildServiceProvider();

        var code = await XdCommand.RunAsync(["options", "phase-check", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("NOT EVALUABLE")]
    [InlineData("NOT MET")]
    public async Task The_exit_code_is_zero_whatever_the_verdict(string verdict)
    {
        var fake = new Fake(_ => Report(verdict));

        var (code, output, error) = await RunAsync(fake);

        code.ShouldBe(0);
        output.ShouldContain("overall: " + verdict);
        output.ShouldContain("limits:");
        error.ShouldBeEmpty();
        fake.Seen.ShouldHaveSingleItem().Cut.ShouldBeEmpty();
    }

    [Fact]
    public async Task Json_prints_the_same_report_and_the_cut_list_reaches_the_use_case()
    {
        var fake = new Fake(_ => Report("NOT EVALUABLE"));

        var (code, output, _) = await RunAsync(fake, "--json", "--cut", "basis:carry-1,expr:iron_condor");

        code.ShouldBe(0);
        output.ShouldStartWith("{");
        output.ShouldContain("\"verdict\": \"NOT EVALUABLE\"");
        output.ShouldContain("\"limits\"");
        output.ShouldContain("\"stale_after_days\": 90");
        fake.Seen.ShouldHaveSingleItem().Cut.ShouldBe(["basis:carry-1", "expr:iron_condor"]);
    }

    [Fact]
    public async Task A_refusal_of_the_use_case_exits_1_and_a_bad_line_exits_2_without_calling_it()
    {
        var refusing = new Fake(_ => Result.Failure<OptionsPhaseCheckView>(Error.Validation("options.unknown_family", "Unknown family in --cut: nope.")));
        var untouched = new Fake(_ => Report("NOT EVALUABLE"));

        var refused = await RunAsync(refusing, "--cut", "nope");
        var usage = await RunAsync(untouched, "--bogus");

        refused.Code.ShouldBe(1);
        refused.Error.ShouldContain("options.unknown_family");
        usage.Code.ShouldBe(2);
        usage.Error.ShouldContain("usage:");
        untouched.Seen.ShouldBeEmpty();
    }
}
