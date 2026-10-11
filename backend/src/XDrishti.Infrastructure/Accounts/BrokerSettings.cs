using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Accounts;

namespace XDrishti.Infrastructure.Accounts;

internal sealed class BrokerSettings(IOptions<BrokersOptions> options) : IBrokerSettings
{
    public string PublicBaseUrl => options.Value.PublicBaseUrl.TrimEnd('/');
}
