using XDrishti.Infrastructure.Auth;

namespace XDrishti.Infrastructure.Tests.Auth;

public sealed class TokenGeneratorTests
{
    private readonly TokenGenerator _tokens = new();

    [Fact]
    public void Session_tokens_are_long_unique_and_url_safe()
    {
        var all = Enumerable.Range(0, 200).Select(_ => _tokens.NewSessionToken()).ToList();

        all.Distinct().Count().ShouldBe(200);
        all.All(t => t.Length >= 43 && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')).ShouldBeTrue();
    }

    [Fact]
    public void Hash_is_a_stable_sha256_hex_and_differs_from_the_token()
    {
        var token = _tokens.NewSessionToken();

        _tokens.Hash(token).ShouldBe(_tokens.Hash(token));
        _tokens.Hash(token).Length.ShouldBe(64);
        _tokens.Hash(token).ShouldNotBe(token);
        _tokens.Hash("a").ShouldBe("ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb");
    }

    [Fact]
    public void Csrf_token_is_derived_from_the_session_token_only()
    {
        var one = _tokens.NewSessionToken();
        var two = _tokens.NewSessionToken();

        _tokens.DeriveCsrfToken(one).ShouldBe(_tokens.DeriveCsrfToken(one));
        _tokens.DeriveCsrfToken(one).ShouldNotBe(_tokens.DeriveCsrfToken(two));
        _tokens.DeriveCsrfToken(one).ShouldNotBe(one);
    }
}
