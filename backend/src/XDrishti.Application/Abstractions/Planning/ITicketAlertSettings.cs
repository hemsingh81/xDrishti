namespace XDrishti.Application.Abstractions.Planning;

/// <summary>The checked <c>Planning:TicketAlerts:*</c> settings (spec P7-20, ADR 0115). <see cref="Enabled"/> is OFF by default.</summary>
public interface ITicketAlertSettings
{
    /// <summary>OFF: nothing is observed, read, stored or answered.</summary>
    bool Enabled { get; }

    /// <summary>A prompt stays open this many trading days after its session.</summary>
    int PromptTradingDays { get; }

    /// <summary>How long the feed keeps its list of watched tickets.</summary>
    int WatchedCacheSeconds { get; }
}
