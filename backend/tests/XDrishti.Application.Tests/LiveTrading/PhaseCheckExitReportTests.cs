using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-06: exit_confirmed reads the phase check; only a whole-report MET is a pass, a failure or anything else is not.</summary>
public sealed class PhaseCheckExitReportTests
{
    private sealed class Fake(Result<LivePhaseCheckView> result) : IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>>
    {
        public ValueTask<Result<LivePhaseCheckView>> HandleAsync(LivePhaseCheckQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private static Result<LivePhaseCheckView> View(string verdict) =>
        Result.Success(new LivePhaseCheckView("value", new LivePhaseReport(new DateOnly(2026, 1, 1), null, 0, 0, verdict, verdict, [], [], [], [], [], "r", []), string.Empty));

    [Theory]
    [InlineData("MET", true)]
    [InlineData("NOT MET", false)]
    [InlineData("NOT EVALUABLE", false)]
    public async Task Only_met_passes(string verdict, bool met) =>
        (await new PhaseCheckExitReport(new Fake(View(verdict))).CheckAsync(CancellationToken.None)).Met.ShouldBe(met);

    [Fact]
    public async Task A_failed_check_is_not_a_pass() =>
        (await new PhaseCheckExitReport(new Fake(Result.Failure<LivePhaseCheckView>(Error.NotFound("live.not_found", "none")))).CheckAsync(CancellationToken.None)).Met.ShouldBeFalse();
}
