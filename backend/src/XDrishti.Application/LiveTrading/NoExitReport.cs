using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>The small-live exit report (P11-06) does not exist yet, so it can never say MET: <c>exit_confirmed</c>, and with it S3, stays closed until a real report replaces this registration. Not a pass, by design.</summary>
internal sealed class NoExitReport : ILiveExitReport
{
    public ValueTask<LiveExitCheck> CheckAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new LiveExitCheck(false, "the small-live phase check (P11-06) is not built yet, so it cannot be read; an unreadable report is never a pass."));
}
