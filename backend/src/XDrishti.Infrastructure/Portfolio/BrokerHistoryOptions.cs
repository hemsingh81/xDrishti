using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>Deployment settings of the trade and ledger read (configuration section <c>Portfolio:BrokerHistory</c>, ADR 0025).</summary>
internal sealed class BrokerHistoryOptions
{
    public const string SectionName = "Portfolio:BrokerHistory";

    /// <summary>Whether the scheduled service starts enabled; a first-start seed (ADR 0018 §3). "Sync now" works either way.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The least time between two calls to Dhan's trade history and ledger (the spec: at least 250 ms); 0 switches the spacing off (tests).</summary>
    [Range(0, 60_000)]
    public int CallSpacingMs { get; set; } = 250;
}
