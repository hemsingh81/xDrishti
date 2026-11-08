using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Application.Shadow;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line and the text of <c>xd paper shadow verify|summary</c> (spec CF-08 part B): pure, no database.</summary>
public sealed class PaperShadowCommandTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    [Fact]
    public void The_verb_is_found_by_its_name()
    {
        PaperShadowCommand.Handles(["shadow", "verify"]).ShouldBeTrue();
        PaperShadowCommand.Handles(["compare"]).ShouldBeFalse();
    }

    [Fact]
    public void An_intact_chain_prints_the_digest_to_keep_outside_the_database()
    {
        var line = PaperShadowCommand.VerifyLine(new ShadowChainCheckView(5, 31, true, null, Day, new string('a', 64)));

        line.ShouldContain("intact");
        line.ShouldContain("5 session(s), 31 row(s)");
        line.ShouldContain(new string('a', 64));
    }

    [Fact]
    public void A_broken_chain_names_the_first_session_that_does_not_recompute()
    {
        var line = PaperShadowCommand.VerifyLine(new ShadowChainCheckView(5, 31, false, Day.AddDays(-2), Day, new string('a', 64)));

        line.ShouldContain("BROKEN from session 2026-11-03");
    }

    [Fact]
    public void The_summary_prints_both_counts_the_placeholder_the_basis_the_pace_and_the_modelled_fills_line()
    {
        var none = new MetricView("unavailable", null, "no graded shadow trade");
        var view = new ShadowSummaryView(
            true, true, null, "modelled fills; no queue, no latency", "separate count",
            new ShadowCountsView(7, 12, 5.2, 20, 18, 4, 1, 1),
            new ShadowEdgeView(12, 5.2, 0.3, true, "the same-day correlation is a placeholder (0.3) until CF-01 estimates it", 3, 60, null, "No ETA yet: it needs at least 10 graded trades (there are 12)."),
            [new ShadowGroupView("all", 12, new MetricView("value", 0.25, null), new MetricView("value", -0.1, null), new MetricView("value", 0.6, null), new MetricView("value", 0.5, null)), new ShadowGroupView("primary", 0, none, none, none, none)],
            new ShadowCostView("gross: the stored charge sheet flat-10 is not confirmed as the owner's, so the figures are labelled gross", true, ["flat-10"], false),
            new ShadowChainView(3, 1, Day, new string('b', 64)),
            new ShadowSequentialView(true, null, "on_track", "monitoring", "sequential failure test (report only, never a pass)"));

        var text = string.Join('\n', PaperShadowCommand.Lines(view));

        text.ShouldContain("modelled fills; no queue, no latency");
        text.ShouldContain("behavioural (P10, armed primary) 7 | edge (shadow, graded) 12, effective 5.2");
        text.ShouldContain("rho 0.30 (the same-day correlation is a placeholder");
        text.ShouldContain("basis: gross: the stored charge sheet flat-10 is not confirmed");
        text.ShouldContain("all: n 12, mean R 0.250, 90% interval -0.100 to 0.600, hit rate 0.500");
        text.ShouldContain("primary: n 0, mean R unavailable (no graded shadow trade)");
        text.ShouldContain("digest " + new string('b', 64));
        text.ShouldContain("sequential failure test (report only, never a pass)");
    }

    [Fact]
    public void With_no_rows_the_note_is_printed()
    {
        var none = new MetricView("unavailable", null, "no graded shadow trade");
        var view = new ShadowSummaryView(
            false, false, "No shadow row is stored: the switch paper.shadow.enabled is OFF.", "modelled fills; no queue, no latency", "separate count", new ShadowCountsView(0, 0, 0, 0, 0, 0, 0, 0),
            new ShadowEdgeView(0, 0, 0.3, true, "placeholder", 0, 60, null, "No ETA yet"), [new ShadowGroupView("all", 0, none, none, none, none)], new ShadowCostView("none yet", true, [], false), new ShadowChainView(0, 0, null, null), new ShadowSequentialView(false, "no series", null, null, null));

        string.Join('\n', PaperShadowCommand.Lines(view)).ShouldContain("No shadow row is stored");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ServiceProvider Services, StringWriter Out, StringWriter Error) Rig(ShadowChainCheckView check)
    {
        IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView> handler = new FakeVerify(check);
        return (new ServiceCollection().AddSingleton(handler).BuildServiceProvider(), new StringWriter(), new StringWriter());
    }

    private sealed class FakeVerify(ShadowChainCheckView check) : IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView>
    {
        public ValueTask<ShadowChainCheckView> HandleAsync(VerifyShadowChainQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(check);
    }

    [Fact]
    public async Task Verify_exits_0_for_an_intact_chain_and_prints_the_digest()
    {
        var (services, output, error) = Rig(new ShadowChainCheckView(2, 3, true, null, Day, new string('c', 64)));
        await using var _ = services;

        var code = await PaperShadowCommand.RunAsync(["shadow", "verify"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.Done);
        output.ToString().ShouldContain(new string('c', 64));
    }

    [Fact]
    public async Task Verify_exits_4_for_a_broken_chain_and_names_the_session()
    {
        var (services, output, error) = Rig(new ShadowChainCheckView(2, 3, false, Day, Day, new string('c', 64)));
        await using var _ = services;

        var code = await PaperShadowCommand.RunAsync(["shadow", "verify"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.NotPassing);
        output.ToString().ShouldContain("BROKEN from session 2026-11-05");
    }

    [Fact]
    public async Task Verify_exits_4_when_the_chain_is_intact_but_the_digest_log_diverges()
    {
        var (services, output, error) = Rig(new ShadowChainCheckView(2, 3, true, null, Day, new string('c', 64), 0, null, "The digest of the session 2026-11-05 in the log differs from the stored one."));
        await using var _ = services;

        var code = await PaperShadowCommand.RunAsync(["shadow", "verify"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.NotPassing);
        output.ToString().ShouldContain("digest log DIVERGED");
    }

    [Theory]
    [InlineData("shadow")]
    [InlineData("shadow", "verify", "extra")]
    [InlineData("shadow", "other")]
    public async Task Bad_arguments_exit_2_with_the_usage(params string[] args)
    {
        var (services, output, error) = Rig(new ShadowChainCheckView(0, 0, true, null, null, null));
        await using var _ = services;

        var code = await PaperShadowCommand.RunAsync(args, services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.UsageError);
        error.ToString().ShouldContain("usage:");
        output.ToString().ShouldBeEmpty();
    }
}
