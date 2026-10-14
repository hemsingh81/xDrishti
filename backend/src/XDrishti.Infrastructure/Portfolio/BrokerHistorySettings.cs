using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class BrokerHistorySettings(IOptions<BrokerHistoryOptions> options) : IBrokerHistorySettings
{
    public bool Enabled => options.Value.Enabled;
}
