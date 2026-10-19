using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// The second source from Dhan's <c>charts/intraday</c> (spec P3-09c D1) through the existing <see cref="IIntradayBarSource"/>: one window of one India day per (symbol, date); the source owns the token, the call spacing and the one retry, and never logs the token.
/// A refused token (401/403) ends the calls: every later day is unavailable without a request. An empty answer, any other failure and a partial day (no bar in the first 15 minutes, or none after 15:00) are "unavailable" for that day, never an exception.
/// </summary>
internal sealed class DhanSecondSource(IFetchTargets targets, IIntradayBarSource source) : ISecondSource
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private IReadOnlyList<FetchTarget>? _targets;

    private bool _refused;

    public string Name => "dhan";

    public async ValueTask<SecondDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        if (_refused)
        {
            return SecondDay.Missing("Dhan refused the access token earlier in this run");
        }

        _targets ??= await targets.ListAsync(cancellationToken);
        var target = _targets.FirstOrDefault(t => string.Equals(t.Symbol, symbol, StringComparison.Ordinal));
        if (target is null)
        {
            return SecondDay.Missing("the instrument is not tracked with a Dhan security id");
        }

        var window = new FetchWindow(new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), India), new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 45)), India));
        var plan = new IntradayFetchPlan([new IntradayTarget(target.InstrumentId, target.Symbol, target.Type, target.SecurityId, target.Segment, [window])]);
        await foreach (var result in source.FetchAsync(plan, cancellationToken))
        {
            if (result.Failure is { } failure)
            {
                _refused |= failure.Kind == IntradayFailureKind.Unauthorized;
                return SecondDay.Missing(failure.Kind == IntradayFailureKind.Unauthorized ? "Dhan refused the access token or the subscription" : "the fetch failed");
            }

            var bars = AssistBarsCsv.OfDate(
                result.Bars.Select(b => new ReaderBar(b.EpochSeconds, Money(b.Open), Money(b.High), Money(b.Low), Money(b.Close), b.Volume)),
                date);
            if (bars is not { Count: > 0 })
            {
                return SecondDay.Missing("Dhan answered with no bars");
            }

            var open = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 15)), India).ToUnixTimeSeconds();
            var late = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), India).ToUnixTimeSeconds();
            return bars[0].EpochSeconds >= open + (15 * 60) || bars[^1].EpochSeconds < late ? SecondDay.Missing("Dhan's answer is partial") : new SecondDay(bars, null);
        }

        return SecondDay.Missing("Dhan gave no result");
    }

    private static decimal Money(double value) => (decimal)Math.Round(value, 4);
}
