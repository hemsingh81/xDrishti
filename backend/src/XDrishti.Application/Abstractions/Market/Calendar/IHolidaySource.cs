namespace XDrishti.Application.Abstractions.Market.Calendar;

/// <summary>NSE's public trading-holiday list (<c>holiday-master?type=trading</c>, JSON), best effort: the public endpoint may refuse a client, so a failure is an answer, not an error. No credentials are sent.</summary>
public interface IHolidaySource
{
    /// <summary>Starts downloading the list; <see cref="SourceDownloadKind.Failed"/> says why in plain words.</summary>
    ValueTask<SourceDownload> OpenHolidaysAsync(CancellationToken cancellationToken);
}
