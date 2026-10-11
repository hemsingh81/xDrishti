using XDrishti.Infrastructure.Accounts;

namespace XDrishti.Infrastructure.Tests.Accounts;

public sealed class BrokersOptionsTests
{
    [Theory]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://localhost:8080/", true)]
    [InlineData("https://xd.example.invalid", true)]
    [InlineData("localhost:8080", false)]
    [InlineData("ftp://localhost", false)]
    [InlineData("http://localhost:8080/app", false)]
    [InlineData("http://localhost:8080/?a=b", false)]
    [InlineData("http://user:pw@localhost:8080", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_public_base_URL_must_be_a_plain_http_origin(string? value, bool valid) =>
        BrokersOptions.IsValidBaseUrl(value).ShouldBe(valid);

    [Fact]
    public void The_default_is_the_local_stack() =>
        BrokersOptions.IsValidBaseUrl(new BrokersOptions().PublicBaseUrl).ShouldBeTrue();
}
