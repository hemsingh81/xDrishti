using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Infrastructure.Market.Eod;

namespace XDrishti.Infrastructure.Market.Lists;

/// <summary><c>Selection:ListFilters:*</c> (DATA-01): the live plan and the replay read the dated lists only while this is on. Default off.</summary>
internal sealed class ListFilterOptions
{
    public const string SectionName = "Selection:ListFilters";

    [Required]
    public bool Enabled { get; set; }
}

internal sealed class ListFilterSettings(IOptions<ListFilterOptions> options) : IListFilterSettings
{
    public bool Enabled => options.Value.Enabled;
}

internal sealed class ListHistorySettings(IOptions<HistoryOptions> options) : IListHistorySettings
{
    public bool Enabled => options.Value.Lists.Enabled;

    public int MaxPerDay => options.Value.Lists.MaxPerDay;

    public TimeSpan Delay => TimeSpan.FromMilliseconds(options.Value.Lists.DelayMs);
}
