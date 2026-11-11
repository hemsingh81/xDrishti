using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Learning;

/// <summary>
/// The configuration section <c>Learning:Universe</c> (BK-11, ADR 0261). Both numbers only decide what the read side calls a tier cell or an observation; neither changes a decision path, a scan scope or a stored row. A value out of range stops start-up.
/// </summary>
public sealed class UniverseSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Learning:Universe";

    /// <summary>Fewest trades a tier cell needs before it can be read in place of its all-shares parent (spec default 30, the report mask).</summary>
    [Range(1, 1000)]
    public int MinTradesChild { get; set; } = 30;

    /// <summary>Fewest trades a tier needs before its edge over the parent is listed as an observation (spec default 100).</summary>
    [Range(10, 10_000)]
    public int LinkMinTrades { get; set; } = 100;
}
