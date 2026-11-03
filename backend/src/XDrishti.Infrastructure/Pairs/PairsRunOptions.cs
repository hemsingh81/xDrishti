using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Pairs;

/// <summary>The settings <c>Pairs:*</c> (P14-13), bound on every host that runs or evaluates the pairs study; bounds are checked at start-up. The charge sheet id is empty by default: a run is then gross and says so.</summary>
internal sealed class PairsRunOptions
{
    public const string SectionName = "Pairs";

    /// <summary>The Y-leg notional in rupees; the X leg is beta times it.</summary>
    [Required]
    [Range(10_000, 10_000_000)]
    public decimal? PairNotional { get; set; } = 100_000m;

    /// <summary>The most candidate pairs one formation keeps (the most correlated).</summary>
    [Required]
    [Range(100, 25_000)]
    public int? MaxCandidates { get; set; } = 2000;

    /// <summary>The stored cost sheet of the owner (intraday equity lines); empty: gross.</summary>
    [MaxLength(40)]
    public string? CostSheetId { get; set; }
}
