using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Market.Eod;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Eod;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>
/// CF-04 criteria 1 and 2 (Part A): the NSE archive fetch goes through the allow-list guard, which ships with an EMPTY pattern list (the owner's security act, ADR 0010, 0017 and 0195): until the owner adds entries a request is refused before
/// anything is sent. With the owner's entries (given here by the test) the file is fetched with a plain GET, a zip is unpacked and a 404 is "no such file".
/// </summary>
public sealed class NseEodSourceTests : HttpMessageHandler, IHttpClientFactory
{
    private static readonly DateOnly Day = new(2024, 7, 5);

    private static readonly AllowedPattern[] OwnerEntries = [new(EodArchive.Host, new Regex("^/(content|archives)/.+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))];

    private readonly List<Uri> _requests = [];

    private IReadOnlyList<AllowedPattern> _patterns = [];

    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("a,b\n")) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(new MasterDataEndpointGuard(_patterns) { InnerHandler = this }, disposeHandler: false);
        MasterDataHttp.ConfigureNse(client);
        return client;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request.RequestUri!);
        return Task.FromResult(_respond());
    }

    private NseEodSource Source() => new(this, new FakeTimeProvider(), NullLogger<NseEodSource>.Instance);

    [Fact]
    public async Task The_shipped_allow_list_has_no_archive_entry_so_the_fetch_is_refused_before_sending()
    {
        MasterDataEndpoints.Patterns.ShouldBeEmpty();

        var fetch = await Source().FetchAsync(EodFileKind.Bhavcopy, Day, Ct);

        fetch.Status.ShouldBe(EodFetchStatus.NotAllowed);
        fetch.Message!.ShouldContain(EodArchive.Host);
        fetch.Message!.ShouldContain("not on the allow-list");
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_owners_entry_the_file_is_fetched_by_a_plain_GET_on_the_archive_host()
    {
        _patterns = OwnerEntries;

        var fetch = await Source().FetchAsync(EodFileKind.Delivery, Day, Ct);

        fetch.Status.ShouldBe(EodFetchStatus.Ok);
        _requests.Single().ToString().ShouldBe("https://nsearchives.nseindia.com/archives/equities/mto/MTO_05072024.DAT");
    }

    [Fact]
    public async Task A_404_is_no_such_file_and_another_status_is_a_failure()
    {
        _patterns = OwnerEntries;
        _respond = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        (await Source().FetchAsync(EodFileKind.Bhavcopy, Day, Ct)).Status.ShouldBe(EodFetchStatus.Absent);

        _respond = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        (await Source().FetchAsync(EodFileKind.Bhavcopy, Day, Ct)).Status.ShouldBe(EodFetchStatus.Failed);

        _respond = () => new HttpResponseMessage(HttpStatusCode.Redirect);
        (await Source().FetchAsync(EodFileKind.Bhavcopy, Day, Ct)).Status.ShouldBe(EodFetchStatus.Failed);
    }

    [Fact]
    public async Task A_zip_is_unpacked_in_memory()
    {
        _patterns = OwnerEntries;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = await zip.CreateEntry("bhav.csv").OpenAsync(Ct);
            await entry.WriteAsync(Encoding.UTF8.GetBytes("SYMBOL,SERIES\nABC,EQ\n"), Ct);
        }

        var bytes = buffer.ToArray();
        _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

        var fetch = await Source().FetchAsync(EodFileKind.Bhavcopy, Day, Ct);

        Encoding.UTF8.GetString(fetch.Content!).ShouldBe("SYMBOL,SERIES\nABC,EQ\n");
    }

    [Fact]
    public async Task A_broken_zip_is_a_failure_not_an_exception()
    {
        var fetch = await NseEodSource.UnpackAsync([(byte)'P', (byte)'K', 3, 4, 9, 9, 9], "/x.zip", Ct);

        fetch.Status.ShouldBe(EodFetchStatus.Failed);
    }

    [Theory]
    [InlineData("https://nsearchives.nseindia.com/content/cm/BhavCopy_NSE_CM_0_0_0_20240708_F_0000.csv.zip", true)]
    [InlineData("https://nsearchives.nseindia.com/content/cm/BhavCopy_NSE_CM_0_0_0_20240708_F_0000.csv.zip?x=1", false)] // no query
    [InlineData("https://nsearchives.nseindia.com.example.invalid/content/x", false)]
    [InlineData("https://www.nseindia.com/content/x", false)]
    [InlineData("http://nsearchives.nseindia.com/content/x", false)]
    public void A_pattern_entry_allows_only_its_host_and_a_full_path_match_with_no_query(string url, bool allowed) =>
        MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri(url), OwnerEntries).ShouldBe(allowed);

    [Fact]
    public void A_pattern_never_allows_a_method_other_than_GET() =>
        MasterDataEndpoints.IsAllowed(HttpMethod.Post, new Uri("https://nsearchives.nseindia.com/content/x"), OwnerEntries).ShouldBeFalse();
}
