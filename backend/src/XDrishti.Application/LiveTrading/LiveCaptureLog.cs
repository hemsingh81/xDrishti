using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Structured log events of the live capture (event ids 5100 to 5109). Counts, codes and ids only - never a symbol, price, quantity, order id, token or URL.</summary>
internal static partial class LiveCaptureLog
{
    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "live.capture_poll outcome={Outcome} fillsNew={FillsNew} ordersNew={OrdersNew}")]
    public static partial void Poll(ILogger logger, string outcome, int fillsNew, int ordersNew);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "live.capture_settle sessions={Sessions} outcomes={Outcomes} pending={Pending} charges={Charges}")]
    public static partial void Settle(ILogger logger, int sessions, int outcomes, int pending, int charges);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information, Message = "live.capture_skipped reason={Reason}")]
    public static partial void Skipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Warning, Message = "live.capture_dropped book={Book} dropped={Dropped} seen={Seen}")]
    public static partial void Dropped(ILogger logger, string book, int dropped, int seen);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Information, Message = "live.settle_history_agreement matched={Matched} missing={Missing} charges={Charges}")]
    public static partial void HistoryAgreement(ILogger logger, int matched, int missing, int charges);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Warning, Message = "live.settle_history_unavailable outcome={Outcome}")]
    public static partial void HistoryUnavailable(ILogger logger, string outcome);
}
