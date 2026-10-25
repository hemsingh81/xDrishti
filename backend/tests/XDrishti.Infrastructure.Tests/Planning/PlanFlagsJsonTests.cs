using Microsoft.Extensions.Logging;
using NSubstitute;
using XDrishti.Domain.Planning;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P8-14: stored flags round-trip, and a value that cannot be read is no flags with one warning, never a failed plan read.</summary>
public sealed class PlanFlagsJsonTests
{
    private static readonly string[] Banner = ["market gate unavailable (VIX not known)"];

    [Fact]
    public void Flags_round_trip_through_the_stored_json()
    {
        var logger = Substitute.For<ILogger>();
        var flags = new PlanBasisFlags(1, "unavailable", ["VIX"], "cut", "applied", 2, "assumed_empty", 0.75, true, "gross", Banner);

        var read = PlanFlagsJson.Read(PlanFlagsJson.Write(flags), logger);

        read.ShouldNotBeNull().Banner.ShouldBe(Banner);
        read.Gate.ShouldBe("unavailable");
        read.RiskMultiplier.ShouldBe(0.75);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"schemaVersion\":\"one\"}")]
    [InlineData("{\"riskMultiplier\":{}}")]
    public void A_value_that_cannot_be_read_is_null_and_logged_once(string json)
    {
        var logger = Substitute.For<ILogger>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        PlanFlagsJson.Read(json, logger).ShouldBeNull();

        logger.ReceivedWithAnyArgs(1).Log(LogLevel.Warning, default, default(object), null, default!);
    }
}
