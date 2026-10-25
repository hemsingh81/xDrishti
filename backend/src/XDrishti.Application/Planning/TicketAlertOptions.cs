using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Planning;

/// <summary>The settings of the price-crossing alerts and post-trade prompts (<c>Planning:TicketAlerts:*</c>, spec P7-20, ADR 0115, doc 16). <see cref="Enabled"/> is OFF by default; no setting here lets an order be placed.</summary>
public sealed class TicketAlertOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Planning:TicketAlerts";

    /// <summary>OFF: no observer is registered, no watched list is read, the notice source answers nothing and the prompt service stores nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>A prompt stays open this many trading days after its session.</summary>
    [Range(1, 10)]
    public int PromptTradingDays { get; set; } = 3;

    /// <summary>How long the feed keeps the list of watched tickets before reading it again (a change of the active set also refreshes it).</summary>
    [Range(5, 120)]
    public int WatchedCacheSeconds { get; set; } = 30;
}
