using XDrishti.Domain.Execution;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>P12-11 criterion 9 and 15: one IPv4 or IPv6 literal and nothing else, stored in its normal form.</summary>
public sealed class IpLiteralTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001", "2001:db8::1")]
    [InlineData("::1", "::1")]
    [InlineData("::", "::")]
    [InlineData("2001:db8:0:0:1:0:0:1", "2001:db8::1:0:0:1")]
    [InlineData("2001:DB8::ABCD", "2001:db8::abcd")]
    [InlineData("1:2:3:4:5:6:7:8", "1:2:3:4:5:6:7:8")]
    [InlineData("::ffff:203.0.113.7", "::ffff:cb00:7107")]
    public void A_literal_is_accepted_in_its_normal_form(string text, string normal)
    {
        IpLiteral.TryNormalize(text, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(normal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("203.0.113")]
    [InlineData("1")]
    [InlineData("127.1")]
    [InlineData("203.0.113.256")]
    [InlineData("203.0.113.7 ")]
    [InlineData("203.0.113.7:443")]
    [InlineData("203.0.113.0/24")]
    [InlineData("fe80::1%eth0")]
    [InlineData("example.com")]
    [InlineData("http://203.0.113.7")]
    [InlineData("0xcb.0.113.7")]
    [InlineData("203.0.113.07")]
    [InlineData("1::2::3")]
    [InlineData("1:2:3:4:5:6:7")]
    [InlineData("1:2:3:4:5:6:7:8:9")]
    [InlineData("12345::1")]
    [InlineData(":1:2:3:4:5:6:7")]
    [InlineData("1.2.3.4::")]
    [InlineData("::203.0.113.7:1")]
    [InlineData("::g")]
    public void Anything_else_is_refused(string? text)
    {
        IpLiteral.TryNormalize(text, out var normalized).ShouldBeFalse();
        normalized.ShouldBeEmpty();
    }
}
