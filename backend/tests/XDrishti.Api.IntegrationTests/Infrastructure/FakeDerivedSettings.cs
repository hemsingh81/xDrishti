using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>The derived baskets' settings (BK-03): OFF until a test turns it on, as in production.</summary>
public sealed class FakeDerivedSettings : IDerivedSettings
{
    public bool Enabled { get; set; }

    public int MaxBackfillDays { get; set; } = 400;

    public int ScoresKeptBeyondMembers { get; set; } = 20;

    public string Snapshot() => "{}";

    public void Reset()
    {
        Enabled = false;
        MaxBackfillDays = 400;
        ScoresKeptBeyondMembers = 20;
    }
}
