using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd paper phase-check</c> (spec P10-06): pure parsing, no database.</summary>
public sealed class PaperPhaseCheckCommandTests
{
    [Fact]
    public void No_options_means_the_last_closed_session_and_the_newest_epoch()
    {
        PaperPhaseCheckCommand.TryParse([], out var through, out var epoch).ShouldBeTrue();

        through.ShouldBeNull();
        epoch.ShouldBeNull();
    }

    [Fact]
    public void Through_and_epoch_are_read()
    {
        PaperPhaseCheckCommand.TryParse(["--through", "2027-01-25", "--epoch", "2"], out var through, out var epoch).ShouldBeTrue();

        through.ShouldBe(new DateOnly(2027, 1, 25));
        epoch.ShouldBe(2);
    }

    [Theory]
    [InlineData("--through", "25-01-2027")]
    [InlineData("--through", "2027-02-30")]
    [InlineData("--epoch", "-1")]
    [InlineData("--epoch", "x")]
    [InlineData("--draws", "5")]
    public void A_bad_line_is_a_usage_error(string flag, string value) =>
        PaperPhaseCheckCommand.TryParse([flag, value], out _, out _).ShouldBeFalse();

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_a_usage_error()
    {
        PaperPhaseCheckCommand.TryParse(["--through"], out _, out _).ShouldBeFalse();
        PaperPhaseCheckCommand.TryParse(["--epoch", "1", "--epoch", "2"], out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_verb_is_found_by_its_name()
    {
        PaperPhaseCheckCommand.Handles(["phase-check"]).ShouldBeTrue();
        PaperPhaseCheckCommand.Handles(["compare"]).ShouldBeFalse();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCheck(Result<PaperPhaseCheckView> result) : IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>>
    {
        public ValueTask<Result<PaperPhaseCheckView>> HandleAsync(EvaluatePaperPhaseCheckQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class FakeSequential(Func<Result<SequentialStatusView>> answer) : IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<SequentialStatusView>> HandleAsync(GetSequentialStatusQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(answer());
        }
    }

    private static Result<SequentialStatusView> View()
    {
        var series = new SequentialSeriesView("basis", null, "monitoring", "monitoring", 0, 0, 0, null, null, null, null, 0, null, new SequentialPathView(0, 0, 1.5));
        return Result.Success(new SequentialStatusView("system", null, 1, true, new SequentialBoundariesView(-1.5, -2.9, 2.7, 4.75, 19), 0.3, true, new string('a', 64), "mu1=0.2", series, series, [], "THE SEQUENTIAL LINE"));
    }

    private static (ServiceProvider Services, StringWriter Out, StringWriter Error, FakeSequential Sequential) Rig(Func<Result<SequentialStatusView>> sequential)
    {
        var fake = new FakeSequential(sequential);
        IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>> check = new FakeCheck(Result.Success(new PaperPhaseCheckView(null!, "THE CHECK TEXT.")));
        IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>> seq = fake;
        return (new ServiceCollection().AddSingleton(check).AddSingleton(seq).BuildServiceProvider(), new StringWriter(), new StringWriter(), fake);
    }

    [Fact]
    public async Task The_sequential_line_follows_the_check_text_which_is_printed_unchanged()
    {
        var (services, output, error, _) = Rig(View);
        await using var _ = services;

        var code = await PaperPhaseCheckCommand.RunAsync(["phase-check"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.Done);
        output.ToString().ShouldStartWith("THE CHECK TEXT.");
        output.ToString().ShouldBe("THE CHECK TEXT.THE SEQUENTIAL LINE" + Environment.NewLine);
        error.ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("--through", "2026-11-04")]
    [InlineData("--epoch", "1")]
    public async Task The_sequential_line_is_skipped_when_through_or_epoch_is_given(string flag, string value)
    {
        var (services, output, error, sequential) = Rig(View);
        await using var _ = services;

        var code = await PaperPhaseCheckCommand.RunAsync(["phase-check", flag, value], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.Done);
        output.ToString().ShouldBe("THE CHECK TEXT.");
        sequential.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_failing_sequential_read_leaves_the_exit_code_and_the_check_text_alone_and_notes_it_on_stderr()
    {
        var (services, output, error, _) = Rig(() => throw new InvalidOperationException("secret detail"));
        await using var _ = services;

        var code = await PaperPhaseCheckCommand.RunAsync(["phase-check"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.Done);
        output.ToString().ShouldBe("THE CHECK TEXT.");
        error.ToString().ShouldBe("sequential line unavailable" + Environment.NewLine);
    }

    [Fact]
    public async Task A_refusal_from_the_sequential_handler_is_the_same_stderr_note_and_changes_nothing_else()
    {
        var (services, output, error, _) = Rig(() => Result.Failure<SequentialStatusView>(Error.Validation("paper.invalid", "refused")));
        await using var _ = services;

        var code = await PaperPhaseCheckCommand.RunAsync(["phase-check"], services, new SimulateConsole(output, error), Ct);

        code.ShouldBe(XdCommand.Done);
        output.ToString().ShouldBe("THE CHECK TEXT.");
        error.ToString().ShouldBe("sequential line unavailable" + Environment.NewLine);
    }

    [Fact]
    public async Task A_cancellation_of_the_sequential_read_is_a_cancellation_not_a_note()
    {
        using var cancel = new CancellationTokenSource();
        var (services, output, error, _) = Rig(() =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });
        await using var _ = services;

        var code = await PaperPhaseCheckCommand.RunAsync(["phase-check"], services, new SimulateConsole(output, error), cancel.Token);

        code.ShouldBe(XdCommand.Cancelled);
        error.ToString().ShouldNotContain("sequential line unavailable");
    }
}
