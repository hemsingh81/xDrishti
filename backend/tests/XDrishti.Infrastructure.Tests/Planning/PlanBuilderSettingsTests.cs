using Microsoft.Extensions.Configuration;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

public sealed class PlanBuilderSettingsTests
{
    private static bool Read(string? value)
    {
        var data = new Dictionary<string, string?>();
        if (value is not null)
        {
            data["Planning:Builder:UseSelection"] = value;
        }

        return new PlanBuilderSettings(new ConfigurationBuilder().AddInMemoryCollection(data).Build()).UseSelection;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void The_flag_is_read_from_configuration_and_defaults_to_off(string? value, bool expected) =>
        Read(value).ShouldBe(expected);

    [Theory]
    [InlineData("yes")]
    [InlineData("1x")]
    [InlineData("garbage")]
    public void Garbage_reads_as_off(string value) => Read(value).ShouldBeFalse();
}
