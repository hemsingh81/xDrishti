using XDrishti.Infrastructure.Evidence;

namespace XDrishti.Infrastructure.Tests.Evidence;

/// <summary>The <c>mem_limit</c> read of <c>xd evidence preflight</c> (spec EV-01): one service's block of a compose file.</summary>
public sealed class ComposeMemLimitTests
{
    private const string Compose = """
        name: xdrishti
        services:
          xd-db:
            mem_limit: 4g
          xd-api:
            image: x
            networks: [back, egress]
            # a comment
            mem_limit: 1g # the heap is about 768 MiB
            depends_on:
              a: b
          xd-worker:
            mem_limit: 2g
        networks:
          back:
        """;

    [Fact]
    public void The_limit_of_the_named_service_is_read_in_mebibytes()
    {
        ComposeMemLimit.Mib(Compose, "xd-api").ShouldBe(1024);
    }

    [Fact]
    public void Another_service_has_its_own_limit()
    {
        ComposeMemLimit.Mib(Compose, "xd-worker").ShouldBe(2048);
    }

    [Theory]
    [InlineData("512m", 512)]
    [InlineData("512mb", 512)]
    [InlineData("1.5g", 1536)]
    [InlineData("\"2g\"", 2048)]
    [InlineData("536870912", 512)]
    [InlineData("2048k", 2)]
    public void The_usual_notations_are_understood(string value, long mib)
    {
        ComposeMemLimit.Mib($"services:\n  xd-api:\n    mem_limit: {value}\n", "xd-api").ShouldBe(mib);
    }

    [Theory]
    [InlineData("lots")]
    [InlineData("")]
    [InlineData("0g")]
    [InlineData("${XD_MEM}")]
    public void A_value_that_cannot_be_parsed_is_null(string value)
    {
        ComposeMemLimit.Mib($"services:\n  xd-api:\n    mem_limit: {value}\n", "xd-api").ShouldBeNull();
    }

    [Fact]
    public void A_missing_service_or_limit_is_null()
    {
        ComposeMemLimit.Mib(Compose, "xd-nothing").ShouldBeNull();
        ComposeMemLimit.Mib("services:\n  xd-api:\n    image: x\n", "xd-api").ShouldBeNull();
    }
}
