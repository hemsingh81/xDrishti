using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class SecretValueTests
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    [Fact]
    public void The_value_never_shows_in_its_text_form()
    {
        SecretValue.TryCreate(Marker, out var value, out _).ShouldBeTrue();

        value.ToString().ShouldBe(SecretValue.Redacted);
        $"{value}".ShouldNotContain(Marker);
        value.Reveal().ShouldBe(Marker);
    }

    [Fact]
    public void Surrounding_spaces_are_removed() =>
        (SecretValue.TryCreate($"  {Marker}\n", out var value, out _) ? value.Reveal() : null).ShouldBe(Marker);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    public void Empty_values_and_values_with_spaces_inside_are_refused_without_echoing_them(string? text)
    {
        SecretValue.TryCreate(text, out var value, out var error).ShouldBeFalse();

        value.ShouldBeNull();
        error.ShouldNotBeNullOrEmpty();
        if (!string.IsNullOrWhiteSpace(text))
        {
            error.ShouldNotContain(text.Trim());
        }
    }

    [Fact]
    public void Values_are_limited_in_length()
    {
        SecretValue.TryCreate(new string('a', SecretValue.MaxLength), out _, out _).ShouldBeTrue();
        SecretValue.TryCreate(new string('a', SecretValue.MaxLength + 1), out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Every_secret_name_has_a_fixed_storage_key()
    {
        Enum.GetValues<BrokerSecretName>().Select(BrokerSecretNames.Key).ShouldBe(["api_key", "api_secret", "access_token"]);
        BrokerSecretNames.TryParseKey("access_token", out var name).ShouldBeTrue();
        name.ShouldBe(BrokerSecretName.AccessToken);
        BrokerSecretNames.TryParseKey("client-id", out _).ShouldBeFalse();
    }
}
