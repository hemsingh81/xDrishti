using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary><see cref="IResultFactsAsOf"/> (P15-03): reads the stored envelopes and facts with a time at or before <c>t - lag</c> and lets <see cref="ResultFactsAsOfRule"/> decide.</summary>
internal sealed class ResultFactsAsOf(IResultFactRows rows, IFilingSettings settings) : IResultFactsAsOf
{
    public async ValueTask<ResultFactsAnswer> Facts(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken)
    {
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        var stored = await rows.ForInstrumentAsync(instrumentId, t - lag, ResultsParser.Version, cancellationToken);
        return ResultFactsAsOfRule.Compute(stored, t, lag) with { ParserVersion = ResultsParser.Version };
    }
}
