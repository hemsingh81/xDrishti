using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary><c>Live:Buckets</c> (spec P11-02): the turnover edges in crore rupees between the liquidity buckets. Unset means the default 10, 50, 150, 500; set, they must be 3 to 6 strictly ascending positive values. Printed on every report.</summary>
public sealed class LiveBucketOptions
{
    public const string SectionName = "Live:Buckets";

    public decimal[]? EdgesCr { get; set; }

    public static bool IsValid(LiveBucketOptions options) => options.EdgesCr is null || BucketEdges.Create(options.EdgesCr) is not null;

    public const string InvalidMessage = "Live:Buckets:EdgesCr must be 3 to 6 strictly ascending positive values (crore rupees).";
}

/// <summary><c>Live:Estimates</c> (spec P11-02): <see cref="Enabled"/> only seeds the weekly service at its first start (OFF, so it starts paused); <see cref="MinN"/> is the fewest observations of a row that gets a mean; <see cref="ProposalMinN"/> the fewest for a proposal sentence.</summary>
public sealed class LiveEstimatesOptions
{
    public const string SectionName = "Live:Estimates";

    public bool Enabled { get; set; }

    [Range(6, 1000)]
    public int MinN { get; set; } = 10;

    [Range(6, 10000)]
    public int ProposalMinN { get; set; } = 20;
}

/// <summary><c>Live:Capacity</c> (spec P11-02): the strength of the prior a bucket is shrunk toward its parent with, and the fewest counted orders for a score.</summary>
public sealed class LiveCapacityOptions
{
    public const string SectionName = "Live:Capacity";

    [Range(1, 50)]
    public int PriorStrength { get; set; } = 10;

    [Range(1, 1000)]
    public int MinN { get; set; } = 10;
}

/// <summary>The settings port over the three sections.</summary>
internal sealed class ConfiguredLiveEstimateSettings(IOptions<LiveBucketOptions> buckets, IOptions<LiveEstimatesOptions> estimates, IOptions<LiveCapacityOptions> capacity) : ILiveEstimateSettings
{
    public bool Enabled => estimates.Value.Enabled;

    public BucketEdges Edges => buckets.Value.EdgesCr is { } e ? BucketEdges.Create(e) ?? BucketEdges.Default : BucketEdges.Default;

    public int MinN => estimates.Value.MinN;

    public int ProposalMinN => estimates.Value.ProposalMinN;

    public int CapacityPriorStrength => capacity.Value.PriorStrength;

    public int CapacityMinN => capacity.Value.MinN;
}
