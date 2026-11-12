using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Market.Lists;
using XDrishti.Infrastructure.Market;
using XDrishti.Infrastructure.Market.Eod;
using XDrishti.Infrastructure.Market.Lists;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>DATA-01 (ADR 0274) criteria 1, 2 and 8: both switches default OFF, the dated ban file is refused by the shipped (empty) allow-list before anything is sent, and with the owner's entry it is one plain GET (404 is no file, anything else is a failure).</summary>
public sealed class NseListArchiveSourceTests : HttpMessageHandler, IHttpClientFactory
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    private static readonly AllowedPattern[] OwnerEntries = [new(ListArchive.Host, new Regex(@"^/archives/fo/sec_ban/fo_secban_\d{8}\.csv$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))];

    private readonly List<HttpRequestMessage> _requests = [];

    private IReadOnlyList<AllowedPattern> _patterns = [];

    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Securities in Ban For Trade Date 02-NOV-2026:\r\n")) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(new MasterDataEndpointGuard(_patterns) { InnerHandler = this }, disposeHandler: false);
        MasterDataHttp.ConfigureNse(client);
        return client;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return Task.FromResult(_respond());
    }

    private NseListArchiveSource Source() => new(this, new FakeTimeProvider(), NullLogger<NseListArchiveSource>.Instance);

    [Fact]
    public void Both_switches_default_to_off()
    {
        new ListHistorySettings(Options.Create(new HistoryOptions())).Enabled.ShouldBeFalse();
        new ListFilterSettings(Options.Create(new ListFilterOptions())).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task The_shipped_allow_list_refuses_the_dated_file_before_anything_is_sent()
    {
        MasterDataEndpoints.Patterns.ShouldBeEmpty();

        var fetch = await Source().FetchBanAsync(Day, Ct);

        fetch.Status.ShouldBe(ListFetchStatus.NotAllowed);
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_owners_entry_the_file_is_one_plain_GET_without_cookie_or_body()
    {
        _patterns = OwnerEntries;

        var fetch = await Source().FetchBanAsync(Day, Ct);

        fetch.Status.ShouldBe(ListFetchStatus.Ok);
        _requests.Count.ShouldBe(1);
        _requests[0].Method.ShouldBe(HttpMethod.Get);
        _requests[0].RequestUri!.ToString().ShouldBe("https://nsearchives.nseindia.com/archives/fo/sec_ban/fo_secban_02112026.csv");
        _requests[0].Headers.Contains("Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task A_404_is_no_file_and_any_other_status_is_a_failure()
    {
        _patterns = OwnerEntries;
        _respond = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        var absent = await Source().FetchBanAsync(Day, Ct);
        _respond = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        var failed = await Source().FetchBanAsync(Day, Ct);

        absent.Status.ShouldBe(ListFetchStatus.Absent);
        failed.Status.ShouldBe(ListFetchStatus.Failed);
    }
}
