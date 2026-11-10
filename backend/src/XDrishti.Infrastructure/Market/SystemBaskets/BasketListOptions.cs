using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.Baskets;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

/// <summary>The settings <c>Baskets:System:Lists:*</c> (BK-05), bounds checked at start-up. The two NSE file fetches are OFF by default; the user agent is the one of <c>Baskets:System:Fetch</c>. Nothing here is a secret.</summary>
internal sealed class BasketListOptions
{
    public const string SectionName = "Baskets:System:Lists";

    /// <summary>Default false: neither <c>fo_secban.csv</c> nor <c>fo_mktlots.csv</c> is ever requested.</summary>
    public bool FetchEnabled { get; set; }

    /// <summary>India times (HH:mm) at which the ban list is asked: the evening publication and the morning retry.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(4)]
    public string[] BanRunTimes { get; set; } = ["19:00", "08:30"];

    /// <summary>India time (HH:mm) at which the lot-size file is asked, after the 08:45 master.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public string LotSizeRunAt { get; set; } = "08:50";
}

internal sealed class BasketListSettings(IOptions<BasketListOptions> options) : IListSettings
{
    private BasketListOptions Value => options.Value;

    public bool FetchEnabled => Value.FetchEnabled;

    public IReadOnlyList<TimeOnly> BanRunTimes => [.. Value.BanRunTimes.Select(t => TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture))];

    public TimeOnly LotSizeRunAt => TimeOnly.ParseExact(Value.LotSizeRunAt, "HH:mm", CultureInfo.InvariantCulture);
}
