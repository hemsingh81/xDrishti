namespace XDrishti.Domain.Tests.Accounts;

/// <summary>How an account recognises the holder of a Dhan token.</summary>
public sealed class BrokerClientIdTests
{
    [Theory]
    [InlineData("1000000001", true)]
    [InlineData(" 1000000001 ", true)]
    [InlineData("1000000002", false)]
    [InlineData(null, false)]
    public void The_client_id_is_recognised_ignoring_surrounding_spaces(string? reported, bool same) =>
        AccountData.New().IsClientId(reported).ShouldBe(same);

    [Fact]
    public void Letters_of_a_client_id_are_compared_without_case() =>
        AccountData.New(clientId: "AB12CD").IsClientId("ab12cd").ShouldBeTrue();
}
