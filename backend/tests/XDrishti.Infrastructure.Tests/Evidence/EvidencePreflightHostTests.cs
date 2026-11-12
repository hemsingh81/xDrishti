using Microsoft.Extensions.Configuration;
using XDrishti.Infrastructure.Evidence;
using XDrishti.Infrastructure.Market.Eod;

namespace XDrishti.Infrastructure.Tests.Evidence;

/// <summary>The NSE switch of <c>xd evidence preflight</c> must read the same key the history options bind (spec EV-01, risk of a misspelt key reading as off).</summary>
public sealed class EvidencePreflightHostTests
{
    [Fact]
    public void The_key_is_the_one_of_the_history_options()
    {
        EvidencePreflightHost.NseKey.ShouldBe("Data:History:Nse:Enabled");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [EvidencePreflightHost.NseKey] = "true" }).Build();

        configuration.GetSection(HistoryOptions.SectionName).Get<HistoryOptions>()!.Nse.Enabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void The_switch_is_read_from_the_key(string? value, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(value is null ? [] : new Dictionary<string, string?> { ["Data:History:Nse:Enabled"] = value }).Build();

        new EvidencePreflightHost(null!, configuration).NseHistoryEnabled().ShouldBe(expected);
    }
}
