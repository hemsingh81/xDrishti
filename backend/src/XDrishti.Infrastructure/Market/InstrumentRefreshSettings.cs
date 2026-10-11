using System.Globalization;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Infrastructure.Market;

internal sealed class InstrumentRefreshSettings(IOptions<InstrumentRefreshOptions> options) : IInstrumentRefreshSettings
{
    public bool AutoRefreshEnabled => options.Value.Enabled;

    public TimeOnly AutoRefreshTimeIst =>
        TimeOnly.ParseExact(options.Value.TimeIst, InstrumentRefreshOptions.TimeFormat, CultureInfo.InvariantCulture);
}
