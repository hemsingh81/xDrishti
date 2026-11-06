using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verbs <c>xd learn loop</c> and <c>xd learn curve</c> (spec CF-12, part 12c-1) over a fake use case: the all-unavailable report of today, an available report, the curve-only cut, the refusal and a bad line.</summary>
public sealed class LearnLoopCommandTests
{
    private const string ScoresReason = "no per-candidate scores are stored: a shadow candidate records neither the model that scored it nor its score";

    private const string CurveReason = "the refit loop that produces the gains of the learning curve is not built";

    private sealed class Fake(Result<LoopReportView> answer) : IQueryHandler<GetLoopReportQuery, Result<LoopReportView>>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<LoopReportView>> HandleAsync(GetLoopReportQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(answer);
        }
    }

    private static readonly LoopSettingsView Settings = new(20, 2000, 0.9, 0.95, 0.002, 0.01, 200, 0.8, 1.2, 2, new string('d', 64), "min_days=20");

    private static readonly string[] Statements = ["Report only: this changes no ranking, size, ticket, state or setting and places no order. A section whose input has no source yet says unavailable and why; unavailable is never read as fine."];

    private static LoopReportView Unavailable() => new(
        Settings,
        new LoopArmsView("unavailable", ScoresReason, 0, null, null, null, null, null),
        new LoopCurveView("unavailable", CurveReason, null, 0, null, null, null, null, "unavailable: " + CurveReason),
        new LoopSlopeView("unavailable", "no outcome is stored with the probability a model gave before it", "unavailable", 0, null, null, null, "unavailable"),
        new LoopLiftView("unavailable", "no shadow candidate stores a score, so no night can be ranked by it", 0, 0, null, null, null, 0, 0, null, "unavailable"),
        Statements, "learning loop (report only): retrained vs frozen unavailable");

    private static LoopReportView Available() => Unavailable() with
    {
        Arms = new LoopArmsView(
            "available", null, 80, 0.10, 0.12, 0.05,
            new LoopArmView("retrained_vs_frozen", "no_measurable_difference", 80, 25, 0.02, -0.05, 0.09, 0.62, 0.38, "No measurable difference."),
            new LoopArmView("retrained_vs_no_ml", "inconclusive", 80, 12, 0.07, -0.1, 0.24, 0.8, 0.2, "Fewer than 20 days: inconclusive.")),
        Curve = new LoopCurveView("available", null, "unclear", 3, 0.0045, null, 203.7, 53.7, "Three points: the slope is shown as unclear."),
    };

    // the text is wrapped to a terminal width, so a sentence is compared with its line breaks and indents folded to one space
    private static async Task<(int Code, string Out, string Error)> RunAsync(Fake fake, params string[] args)
    {
        var (code, output, error) = await RunRawAsync(fake, args);

        return (code, string.Join(' ', output.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries)), error);
    }

    private static async Task<(int Code, string Out, string Error)> RunRawAsync(Fake fake, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection().AddSingleton<IQueryHandler<GetLoopReportQuery, Result<LoopReportView>>>(fake).BuildServiceProvider();

        var code = await XdCommand.RunAsync(["learn", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Today_every_section_says_unavailable_and_why_and_none_is_a_pass()
    {
        var (code, output, error) = await RunAsync(new Fake(Result.Success(Unavailable())), "loop");

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldContain("three arms: unavailable: no per-candidate scores are stored");
        output.ShouldContain("learning curve: unavailable: the refit loop that produces the gains of the learning curve is not built.");
        output.ShouldContain("reliability slope: unavailable: no outcome is stored with the probability a model gave before it.");
        output.ShouldContain("decision-quality lift: unavailable: no shadow candidate stores a score");
        output.ShouldContain("Unavailable is not a finding and not a pass.");
        output.ShouldNotContain("helps");
        output.ShouldNotContain("no_measurable_difference");
    }

    [Fact]
    public async Task An_available_report_shows_both_comparisons_with_their_numbers()
    {
        var (code, output, _) = await RunAsync(new Fake(Result.Success(Available())), "loop");

        code.ShouldBe(0);
        output.ShouldContain("three arms: 80 candidate(s) in the common set; mean value per candidate: frozen 0.1000, retrained 0.1200, no model 0.0500");
        output.ShouldContain("retrained vs frozen: no_measurable_difference, mean difference 0.0200 per candidate, 90% interval [-0.0500, 0.0900], P(better) 0.620, P(worse) 0.380, 80 candidate(s) on 25 day(s)");
        output.ShouldContain("retrained vs no model: inconclusive");
        output.ShouldContain("learning curve: unclear, 3 point(s) fitted, slope 0.0045 per 50 rows (standard error unavailable), projected rows 203.7, more rows 53.7");
    }

    [Fact]
    public async Task A_missing_comparison_says_unavailable_with_a_reason_and_is_not_a_pass()
    {
        var report = Available() with { Arms = Available().Arms with { RetrainedVsNoMl = null } };

        var (code, output, _) = await RunAsync(new Fake(Result.Success(report)), "loop");

        code.ShouldBe(0);
        output.ShouldContain("retrained vs no model: unavailable: the report holds no comparison for these two arms. Unavailable is not a finding and not a pass.");
        output.ShouldContain("retrained vs frozen: no_measurable_difference");
    }

    [Fact]
    public async Task The_curve_verb_prints_only_the_learning_curve_part()
    {
        var (code, output, _) = await RunAsync(new Fake(Result.Success(Unavailable())), "curve");

        code.ShouldBe(0);
        output.ShouldContain("learning curve: unavailable: the refit loop that produces the gains of the learning curve is not built.");
        output.ShouldContain("settings: flat below 0.002 gain per 50 rows, projection target gain 0.01");
        output.ShouldContain("Unavailable is not a finding and not a pass.");
        output.ShouldNotContain("three arms");
        output.ShouldNotContain("reliability slope");
        output.ShouldNotContain("decision-quality lift");
    }

    [Fact]
    public async Task The_curve_verb_shows_an_available_curve()
    {
        var (_, output, _) = await RunAsync(new Fake(Result.Success(Available())), "curve");

        output.ShouldContain("learning curve: unclear, 3 point(s) fitted");
        output.ShouldNotContain("three arms");
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("curve")]
    public async Task A_refusal_exits_1_and_prints_the_reason_on_stderr(string verb)
    {
        var (code, output, error) = await RunAsync(new Fake(Result.Failure<LoopReportView>(Error.Validation("confidence.invalid", "no"))), verb);

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain("confidence.invalid: no");
    }

    [Theory]
    [InlineData("loop", "--strategy", "ORB")]
    [InlineData("curve", "extra")]
    public async Task An_argument_is_a_usage_error_and_the_use_case_is_not_called(params string[] args)
    {
        var fake = new Fake(Result.Success(Unavailable()));

        var (code, _, error) = await RunAsync(fake, args);

        code.ShouldBe(2);
        error.ShouldContain("unknown argument");
        error.ShouldContain("usage:");
        fake.Calls.ShouldBe(0);
    }
}
