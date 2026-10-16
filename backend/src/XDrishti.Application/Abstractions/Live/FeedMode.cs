namespace XDrishti.Application.Abstractions.Live;

/// <summary>How much of an instrument the feed sends (ADR 0024 §2): <see cref="Ticker"/> is the last price and its time (indices, alert-only instruments), <see cref="Quote"/> adds volume and the day's open, high, low and close (holdings).</summary>
public enum FeedMode
{
    Ticker,
    Quote,
}
