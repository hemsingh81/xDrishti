using XDrishti.Domain.Market;

namespace XDrishti.Application.Abstractions.Market;

/// <summary>The two public files the master list is built from (ADR 0010): Dhan's detailed scrip master and NSE's Nifty Total Market list. No credentials are involved.</summary>
public interface IInstrumentSource
{
    /// <summary>Starts downloading Dhan's scrip master. With validators, an unchanged file answers <see cref="SourceDownloadKind.NotModified"/>.</summary>
    ValueTask<SourceDownload> OpenMasterAsync(SourceStamp? known, CancellationToken cancellationToken);

    /// <summary>Starts downloading NSE's sector list (small; always fetched in full).</summary>
    ValueTask<SourceDownload> OpenSectorsAsync(CancellationToken cancellationToken);
}
