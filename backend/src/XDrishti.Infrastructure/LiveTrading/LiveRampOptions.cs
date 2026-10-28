using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// <c>Live:Ramp</c> (spec P11-04, ADR 0148): the owner-fixed ramp factors. Its own sub-section class, not on the <c>Live</c> root (the feed's keys there are not this class's). <see cref="StepFactors"/> unset means 0.25, 0.5, 1.0; set, it must be three rising
/// numbers above 0, each not above the ceiling of its step, or the process does not start.
/// </summary>
public sealed class LiveRampOptions
{
    public const string SectionName = "Live:Ramp";

    /// <summary>S1, S2 and S3 factors; null = the ceilings. (A defaulted array would be appended to by the binder, so the default is applied on read.)</summary>
    public double[]? StepFactors { get; set; }

    public int MinSessions { get; set; } = RampThresholds.Defaults.MinSessions;

    public int MinRoundTripsS1 { get; set; } = RampThresholds.Defaults.MinRoundTripsS1;

    public int MinRoundTripsS2 { get; set; } = RampThresholds.Defaults.MinRoundTripsS2;

    public double MaxSlippageGapR { get; set; } = RampThresholds.Defaults.MaxSlippageGapR;

    public double MaxSlippageUpperR { get; set; } = RampThresholds.Defaults.MaxSlippageUpperR;

    public double MaxDiscretionaryShare { get; set; } = RampThresholds.Defaults.MaxDiscretionaryShare;

    public double MinProtectedShare { get; set; } = RampThresholds.Defaults.MinProtectedShare;

    public double MaxChasedShare { get; set; } = RampThresholds.Defaults.MaxChasedShare;

    public double MinCaptureShare { get; set; } = RampThresholds.Defaults.MinCaptureShare;

    public double MaxDrawdownR { get; set; } = RampThresholds.Defaults.MaxDrawdownR;

    public int HoldCooldownSessions { get; set; } = RampThresholds.Defaults.HoldCooldownSessions;

    /// <summary>The numbers of the gates and breach rules as configured.</summary>
    public RampThresholds ToThresholds() => new()
    {
        MinSessions = MinSessions,
        MinRoundTripsS1 = MinRoundTripsS1,
        MinRoundTripsS2 = MinRoundTripsS2,
        MaxSlippageGapR = MaxSlippageGapR,
        MaxSlippageUpperR = MaxSlippageUpperR,
        MaxDiscretionaryShare = MaxDiscretionaryShare,
        MinProtectedShare = MinProtectedShare,
        MaxChasedShare = MaxChasedShare,
        MinCaptureShare = MinCaptureShare,
        MaxDrawdownR = MaxDrawdownR,
        HoldCooldownSessions = HoldCooldownSessions,
    };

    public static bool IsValid(LiveRampOptions o) => (o.StepFactors is null || RampPolicy.FactorsAreValid(o.StepFactors)) && o.ToThresholds().IsValid;

    public const string InvalidMessage = "Live:Ramp:StepFactors must be three rising numbers above 0, each at most 0.25, 0.5 and 1.0 for S1, S2 and S3, and the gate numbers (MinSessions, MinRoundTrips*, the shares between 0 and 1, MaxSlippageUpperR not below MaxSlippageGapR, MaxDrawdownR above 0) must make sense.";
}

/// <summary>The configured factors, or the ceilings.</summary>
internal sealed class ConfiguredLiveRampSettings(IOptions<LiveRampOptions> options) : ILiveRampSettings
{
    public IReadOnlyList<double> StepFactors => options.Value.StepFactors ?? RampPolicy.DefaultFactors;

    public RampThresholds Thresholds => options.Value.ToThresholds();
}
