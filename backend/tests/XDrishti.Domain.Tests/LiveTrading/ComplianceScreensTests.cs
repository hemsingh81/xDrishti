using XDrishti.Domain.LiveTrading;

namespace XDrishti.Domain.Tests.LiveTrading;

/// <summary>Spec P11-05 criterion 3: the free text of a record holds no address, client id or token; ordinary words, rupees and days pass.</summary>
public sealed class ComplianceScreensTests
{
    [Theory]
    [InlineData("it showed 192.168.1.10 today", "looks like an IP address")]
    [InlineData("10.0.0.1", "looks like an IP address")]
    [InlineData("the address 2001:db8::1 was given", "looks like an IP address")]
    [InlineData("[2001:db8::1],", "looks like an IP address")]
    [InlineData("fe80::1", "looks like an IP address")]
    [InlineData("2001:0db8:0000:0000:0000:ff00:0042:8329", "looks like an IP address")]
    [InlineData("client 1100000001 asked", "looks like a client id")]
    [InlineData("id 1234567.", "looks like a client id")]
    [InlineData("20261024", "looks like a client id")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", "looks like a token")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789ABCD", "looks like a token")]
    [InlineData("key=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "looks like a token")]
    public void Something_that_looks_like_an_identifier_is_refused_with_its_reason(string text, string reason) =>
        ComplianceScreens.Reject(text).ShouldBe(reason);

    [Theory]
    [InlineData("7 days")]
    [InlineData("Rs 350 a month, twelve months upfront")]
    [InlineData("Dhan said reads are not restricted; call at 12:30:45 on 24 Oct")]
    [InlineData("1,00,000 rupees and 123456 is only six digits")]
    [InlineData("version 1.2.3 of the circular")]
    [InlineData("matches")]
    [InlineData("ACT Broadband")]
    [InlineData("see NSE/INVG/73992 and https://dhanhq.co/docs/v2/authentication/")]
    public void Ordinary_words_numbers_and_times_pass(string text) => ComplianceScreens.Reject(text).ShouldBeNull();
}
