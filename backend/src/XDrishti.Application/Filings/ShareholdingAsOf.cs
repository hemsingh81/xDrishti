using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary><see cref="IShareholdingAsOf"/> (P15-04): reads the stored groups with a time at or before <c>t - lag</c> and lets <see cref="ShareholdingAsOfRule"/> decide.</summary>
internal sealed class ShareholdingAsOf(IShareholdingRows rows, IFilingSettings settings) : IShareholdingAsOf
{
    public async ValueTask<ShareholdingAnswer> Latest(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken)
    {
        var lag = TimeSpan.FromMinutes(settings.LagMinutes);
        var stored = await rows.GroupsAsync(instrumentId, t - lag, cancellationToken);
        return ShareholdingAsOfRule.Compute(stored, t, lag);
    }
}
