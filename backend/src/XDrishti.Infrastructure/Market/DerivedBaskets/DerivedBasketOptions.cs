using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Infrastructure.Market.DerivedBaskets;

/// <summary>The settings <c>Baskets:System:Derived:*</c> (BK-03), bounds checked at start-up. The refresh is OFF by default; nothing here is a secret.</summary>
internal sealed class DerivedBasketOptions
{
    public const string SectionName = "Baskets:System:Derived";

    /// <summary>Default false: nothing is read and nothing is written.</summary>
    public bool Enabled { get; set; }

    /// <summary>The most trading days one backfill may cover.</summary>
    [Range(1, 1000)]
    public int MaxBackfillDays { get; set; } = 400;

    /// <summary>The next-ranked non-members stored beside the members of a version.</summary>
    [Range(0, 100)]
    public int ScoresKeptBeyondMembers { get; set; } = 20;
}

internal sealed class DerivedSettings(IOptions<DerivedBasketOptions> options) : IDerivedSettings
{
    private DerivedBasketOptions Value => options.Value;

    public bool Enabled => Value.Enabled;

    public int MaxBackfillDays => Value.MaxBackfillDays;

    public int ScoresKeptBeyondMembers => Value.ScoresKeptBeyondMembers;

    public string Snapshot() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["enabled"] = Enabled.ToString(CultureInfo.InvariantCulture),
        ["maxBackfillDays"] = MaxBackfillDays.ToString(CultureInfo.InvariantCulture),
        ["scoresKeptBeyondMembers"] = ScoresKeptBeyondMembers.ToString(CultureInfo.InvariantCulture),
    });
}
