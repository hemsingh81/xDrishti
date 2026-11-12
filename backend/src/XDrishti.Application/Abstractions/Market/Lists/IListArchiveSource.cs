namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary>The one GET of NSE's dated F&amp;O-ban file (<c>fo_secban_DDMMYYYY.csv</c>) through the guarded history client. GET only, no cookie, no redirect; the allow-list is read, never written.</summary>
public interface IListArchiveSource
{
    ValueTask<ListArchiveFetch> FetchBanAsync(DateOnly date, CancellationToken cancellationToken);
}
