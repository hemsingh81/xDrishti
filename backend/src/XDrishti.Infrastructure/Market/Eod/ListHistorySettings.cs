using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Lists;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary><c>Data:History:Lists:*</c> (DATA-01): lives with the history options it reads.</summary>
internal sealed class ListHistorySettings(IOptions<HistoryOptions> options) : IListHistorySettings
{
    public bool Enabled => options.Value.Lists.Enabled;

    public int MaxPerDay => options.Value.Lists.MaxPerDay;

    public TimeSpan Delay => TimeSpan.FromMilliseconds(options.Value.Lists.DelayMs);
}
