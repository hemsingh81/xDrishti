using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.OptionExpression;

/// <summary>The settings <c>Options:Remark:*</c> (P14-08, ADR 0197), bound on every host that runs the option re-mark; bounds are checked at start-up.</summary>
internal sealed class RemarkOptions
{
    public const string SectionName = "Options:Remark";

    /// <summary>The oldest vendor IV, in minutes before a bar, a model-price fallback may use (5 to 390: a trading day).</summary>
    [Required]
    [Range(5, 390)]
    public int? MaxIvAgeMinutes { get; set; } = 60;
}
