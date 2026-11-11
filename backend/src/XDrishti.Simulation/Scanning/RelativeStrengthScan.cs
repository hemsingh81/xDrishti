using XDrishti.Domain.Market.Strength;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The stored relative-strength rows of one instrument as the frame reads them (BK-09), opaque outside the scanning stage like <see cref="ResultsDaysScan"/>. The frame reads the newest row strictly before each date, so a decision on day D sees the row of the previous
/// closed session and never a later one.
/// </summary>
public sealed class RelativeStrengthScan
{
    private RelativeStrengthScan(RelativeStrengthDays days)
    {
        Days = days;
    }

    internal RelativeStrengthDays Days { get; }

    /// <summary>Whether the side names <c>mom_rank</c>, <c>rs_vs_index</c> or <c>sector_rs</c>.</summary>
    public static bool Reads(CompiledSide side)
    {
        ArgumentNullException.ThrowIfNull(side);
        return side.Features.Any(f => f.Name is "mom_rank" or "rs_vs_index" or "sector_rs");
    }

    /// <summary>The rows of the instrument as the frame's series; unknown feature names are left out.</summary>
    public static RelativeStrengthScan For(RelativeStrengthInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new RelativeStrengthScan(new RelativeStrengthDays(inputs.Points.Select(p => p.Feature switch
        {
            "mom_rank" => (DateFeatureKind?)DateFeatureKind.MomRank,
            "rs_vs_index" => DateFeatureKind.RsVsIndex,
            "sector_rs" => DateFeatureKind.SectorRs,
            _ => null,
        } is { } kind ? new RelativeStrengthPoint(kind, p.P1, p.P2, p.P3, p.AsOf, p.Value) : (RelativeStrengthPoint?)null).OfType<RelativeStrengthPoint>()));
    }
}
