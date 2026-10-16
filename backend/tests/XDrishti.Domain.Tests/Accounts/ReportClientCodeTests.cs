using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class ReportClientCodeTests
{
    private static readonly string Hash = new('a', ReportClientCode.HashLength);

    [Fact]
    public void A_code_is_remembered_as_the_lower_case_hash_it_is_given()
    {
        var at = new DateTimeOffset(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        var account = Guid.NewGuid();

        var code = ReportClientCode.Remember(account, "  " + Hash.ToUpperInvariant() + " ", at);

        (code.AccountId, code.Hash, code.RememberedAt).ShouldBe((account, Hash, at));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB12CD34EF")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaag")]
    public void Anything_but_a_64_character_hexadecimal_hash_is_refused(string hash) =>
        Should.Throw<ArgumentException>(() => ReportClientCode.Remember(Guid.NewGuid(), hash, DateTimeOffset.UnixEpoch));

    [Fact]
    public void A_code_belongs_to_an_account() =>
        Should.Throw<ArgumentException>(() => ReportClientCode.Remember(Guid.Empty, Hash, DateTimeOffset.UnixEpoch));
}
