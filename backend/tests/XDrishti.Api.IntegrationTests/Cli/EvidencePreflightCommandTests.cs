using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Evidence;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The verb <c>xd evidence preflight</c> (spec EV-01) over a fake use case: exit 0 whatever the states, text and JSON from the same items, a golden text, the bad line and the failure have their own codes.</summary>
public sealed class EvidencePreflightCommandTests
{
    private sealed class Fake(Func<EvidencePreflightView> answer) : IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>
    {
        public ValueTask<EvidencePreflightView> HandleAsync(GetEvidencePreflightQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(answer());
    }

    private static readonly EvidencePreflightView Sample = new(
    [
        new EvidencePreflightItem("deployed version", "MET", "applied A, this build A, pending 0", "none"),
        new EvidencePreflightItem("hold-out fixed", "NOT MET", "not fixed: it is fixed at the first Lab post (ADR 0207)", "run a Lab test"),
        new EvidencePreflightItem("shadow switch", "UNAVAILABLE", "the read failed: InvalidOperationException", "check the database"),
    ]);

    private static async Task<(int Code, string Out, string Error)> RunAsync(Func<EvidencePreflightView> answer, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var services = new ServiceCollection().AddSingleton<IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>>(new Fake(answer)).BuildServiceProvider();

        var code = await XdCommand.RunAsync(["evidence", .. args], services, new SimulateConsole(output, error), TimeSpan.Zero, TestContext.Current.CancellationToken);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task The_text_is_one_line_per_item_with_the_fix_under_an_item_that_is_not_met()
    {
        var (code, output, error) = await RunAsync(() => Sample, "preflight");

        code.ShouldBe(0);
        error.ShouldBeEmpty();
        output.ShouldBe(
            "MET         deployed version: applied A, this build A, pending 0\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            + "NOT MET     hold-out fixed: not fixed: it is fixed at the first Lab post (ADR 0207)\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            + "            fix: run a Lab test\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            + "UNAVAILABLE shadow switch: the read failed: InvalidOperationException\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            + "            fix: check the database\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Json_prints_the_same_items_as_an_array()
    {
        var (code, output, _) = await RunAsync(() => Sample, "preflight", "--json");

        code.ShouldBe(0);
        using var document = JsonDocument.Parse(output);
        var items = document.RootElement.EnumerateArray().ToList();
        items.Count.ShouldBe(3);
        items.Select(i => i.GetProperty("item").GetString()).ShouldBe(Sample.Items.Select(i => i.Item));
        items.Select(i => i.GetProperty("state").GetString()).ShouldBe(Sample.Items.Select(i => i.State));
        items[1].GetProperty("reason").GetString().ShouldBe(Sample.Items[1].Reason);
        items[1].GetProperty("fix").GetString().ShouldBe("run a Lab test");
    }

    [Fact]
    public async Task Control_characters_cannot_reach_the_terminal()
    {
        var view = new EvidencePreflightView([new EvidencePreflightItem("x", "MET", "a\u001b[31mred", "none")]);

        var (_, output, _) = await RunAsync(() => view, "preflight");

        output.ShouldNotContain("\u001b");
    }

    [Theory]
    [InlineData("")]
    [InlineData("other")]
    [InlineData("preflight --yaml")]
    [InlineData("preflight --json extra")]
    public async Task A_wrong_line_is_a_usage_error(string line)
    {
        var args = line.Length == 0 ? [] : line.Split(' ');

        var (code, output, error) = await RunAsync(() => Sample, args);

        code.ShouldBe(XdCommand.UsageError);
        output.ShouldBeEmpty();
        error.ShouldContain("usage:");
    }

    [Fact]
    public async Task A_failing_use_case_is_exit_three()
    {
        var (code, _, error) = await RunAsync(() => throw new InvalidOperationException("boom"), "preflight");

        code.ShouldBe(XdCommand.Failed);
        error.ShouldStartWith("failed:");
    }
}
