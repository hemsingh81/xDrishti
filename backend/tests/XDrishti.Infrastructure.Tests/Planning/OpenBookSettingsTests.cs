using Microsoft.Extensions.Configuration;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

public sealed class OpenBookSettingsTests
{
    private static bool Read(string? value)
    {
        var data = new Dictionary<string, string?>();
        if (value is not null)
        {
            data["Planning:OpenBook:AssumedEmpty"] = value;
        }

        return new OpenBookSettings(new ConfigurationBuilder().AddInMemoryCollection(data).Build()).AssumedEmpty;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    [InlineData("True", false)]
    [InlineData("TRUE", false)]
    [InlineData(" true", false)]
    [InlineData("true ", false)]
    [InlineData("1", false)]
    public void The_open_book_is_unavailable_unless_the_owner_sets_it_true(string? value, bool expected) =>
        Read(value).ShouldBe(expected);
}
