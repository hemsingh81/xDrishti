using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Portfolio;

internal sealed class PortfolioRefreshSettings(IOptions<PortfolioRefreshOptions> options) : IPortfolioRefreshSettings
{
    public bool AutoRefreshEnabled => options.Value.Enabled;
}
