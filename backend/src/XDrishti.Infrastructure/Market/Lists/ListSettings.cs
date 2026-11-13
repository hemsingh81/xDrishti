using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Lists;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary><c>Selection:ListFilters:*</c> (DATA-01): the live plan and the replay read the dated lists only while this is on. Default on (ADR 0282).</summary>
internal sealed class ListFilterOptions
{
    public const string SectionName = "Selection:ListFilters";

    [Required]
    public bool Enabled { get; set; } = true;
}

internal sealed class ListFilterSettings(IOptions<ListFilterOptions> options) : IListFilterSettings
{
    public bool Enabled => options.Value.Enabled;
}
