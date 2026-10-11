namespace XDrishti.Application.Tests.Accounts;

public sealed class DhanRedirectHandlerTests
{
    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080/api/brokers/dhan/callback")]
    [InlineData("https://xd.example.invalid/", "https://xd.example.invalid/api/brokers/dhan/callback")]
    public async Task The_redirect_url_is_the_public_base_url_plus_the_callback_path(string baseUrl, string expected)
    {
        var view = await AccountsFixture.DhanRedirect(baseUrl).HandleAsync(new(), CancellationToken.None);

        view.RedirectUrl.ShouldBe(expected);
    }
}
