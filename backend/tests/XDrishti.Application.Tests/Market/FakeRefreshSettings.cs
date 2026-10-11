using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Application.Tests.Market;

internal sealed class FakeRefreshSettings : IInstrumentRefreshSettings
{
    public bool AutoRefreshEnabled { get; set; } = true;

    public TimeOnly AutoRefreshTimeIst { get; set; } = new(8, 45);
}
