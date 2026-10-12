using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Tests.Market;

public sealed class HttpInstrumentSourceTests : IDisposable
{
    private readonly StubMasterData _stub = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpInstrumentSource Source => new(_stub, _stub.Clock, NullLogger<HttpInstrumentSource>.Instance);

    public void Dispose() => _stub.Dispose();

    private static async Task<string> ReadAllAsync(SourceDownload download)
    {
        using var reader = new StreamReader(download.Content!);
        return await reader.ReadToEndAsync(Ct);
    }

    [Fact]
    public async Task The_master_is_fetched_with_a_plain_GET_to_the_exact_URL_and_no_credentials()
    {
        _stub.Respond = _ => StubMasterData.Csv("a,b\n1,2\n", "\"etag-1\"", "Sat, 10 Oct 2026 02:50:08 GMT");

        await using var download = await Source.OpenMasterAsync(null, Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Content);
        (await ReadAllAsync(download)).ShouldBe("a,b\n1,2\n");
        var (method, uri, headers) = _stub.Requests.ShouldHaveSingleItem();
        method.ShouldBe(HttpMethod.Get);
        uri.ToString().ShouldBe("https://images.dhan.co/api-data/api-scrip-master-detailed.csv");
        headers["User-Agent"].ShouldBe("xDrishti/0.1 (personal use)");
        headers.Keys.ShouldNotContain(k => k == "Authorization" || k == "Cookie" || k == "Proxy-Authorization" || k == "access-token" || k == "client-id");
        headers.ShouldNotContainKey("If-None-Match");
        headers.ShouldNotContainKey("If-Modified-Since");
    }

    [Fact]
    public async Task The_validators_of_the_answer_are_returned()
    {
        _stub.Respond = _ => StubMasterData.Csv("x", "\"etag-1\"", "Sat, 10 Oct 2026 02:50:08 GMT");

        await using var download = await Source.OpenMasterAsync(null, Ct);

        download.Stamp.ETag.ShouldBe("\"etag-1\"");
        download.Stamp.LastModified.ShouldBe("Sat, 10 Oct 2026 02:50:08 GMT");
    }

    [Fact]
    public async Task Known_validators_are_sent_and_a_304_means_not_modified()
    {
        _stub.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        await using var download = await Source.OpenMasterAsync(new SourceStamp("\"etag-1\"", "Sat, 10 Oct 2026 02:50:08 GMT"), Ct);

        download.Kind.ShouldBe(SourceDownloadKind.NotModified);
        var headers = _stub.Requests.Single().Headers;
        headers["If-None-Match"].ShouldBe("\"etag-1\"");
        headers["If-Modified-Since"].ShouldContain("10 Oct 2026 02:50:08");
    }

    [Fact]
    public async Task A_weak_ETag_is_sent_back_as_it_was_served()
    {
        _stub.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        await using var download = await Source.OpenMasterAsync(new SourceStamp("W/\"49439-1791603112448\"", null), Ct);

        _stub.Requests.Single().Headers["If-None-Match"].ShouldBe("W/\"49439-1791603112448\"");
    }

    [Fact]
    public async Task A_304_to_a_request_without_validators_is_a_failure_not_an_unchanged_file()
    {
        _stub.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        await using var download = await Source.OpenMasterAsync(null, Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.MovedPermanently, "301")] // redirects are never followed
    public async Task Any_other_status_is_a_failure_that_names_the_status(HttpStatusCode status, string code)
    {
        _stub.Respond = _ => new HttpResponseMessage(status);

        await using var download = await Source.OpenMasterAsync(null, Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
        download.Failure!.ShouldContain(code);
        download.Failure!.ShouldNotContain("images.dhan.co");
    }

    [Fact]
    public async Task A_network_failure_is_reported_without_the_address()
    {
        _stub.Respond = _ => throw new HttpRequestException("connection to https://images.dhan.co/api-data/api-scrip-master-detailed.csv failed");

        await using var download = await Source.OpenMasterAsync(null, Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
        download.Failure!.ShouldNotContain("https://");
    }

    [Fact]
    public async Task The_sector_list_comes_from_the_NSE_archive_and_is_always_fetched_in_full()
    {
        _stub.Respond = _ => StubMasterData.Csv("Company Name,Industry\n", "W/\"1\"");

        await using var download = await Source.OpenSectorsAsync(Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Content);
        var (method, uri, headers) = _stub.Requests.Single();
        (method, uri.ToString()).ShouldBe((HttpMethod.Get, "https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv"));
        headers["User-Agent"].ShouldBe("xDrishti/0.1 (personal use)");
        headers.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_refused_without_reading()
    {
        _stub.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };
            response.Content.Headers.ContentLength = MasterDataHttp.NseMaxBytes + 1;
            return response;
        };

        await using var download = await Source.OpenSectorsAsync(Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
        download.Failure!.ShouldContain("larger than the allowed 5 MB");
    }

    [Fact]
    public async Task A_body_that_grows_past_the_cap_is_cut_off_while_reading()
    {
        await using var inner = new MemoryStream(new byte[2048]);
        await using var guarded = new GuardedStream(inner, 1000, "The test file", CancellationToken.None);
        var buffer = new byte[600];

        (await guarded.ReadAsync(buffer, Ct)).ShouldBe(600);
        var error = await Should.ThrowAsync<SourceReadException>(async () => await guarded.ReadExactlyAsync(buffer, Ct));

        error.Message.ShouldBe("The test file is larger than the allowed 0 MB.");
    }

    [Fact]
    public async Task A_download_past_its_deadline_is_stopped_with_a_clear_message()
    {
        using var deadline = new CancellationTokenSource();
        await using var slow = new NeverEndingStream();
        await using var guarded = new GuardedStream(slow, 1000, "The test file", deadline.Token);
        var read = guarded.ReadAsync(new byte[10], Ct).AsTask();

        await deadline.CancelAsync();

        var error = await Should.ThrowAsync<SourceReadException>(async () => await read);
        error.Message.ShouldBe("The test file took too long to download.");
    }

    [Fact]
    public async Task Cancelling_the_request_itself_is_not_reported_as_a_timeout()
    {
        using var cancel = new CancellationTokenSource();
        await using var slow = new NeverEndingStream();
        await using var guarded = new GuardedStream(slow, 1000, "The test file", CancellationToken.None);
        var read = guarded.ReadAsync(new byte[10], cancel.Token).AsTask();

        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await read);
    }

    [Fact]
    public async Task A_server_that_never_answers_ends_with_a_failure_when_the_deadline_passes()
    {
        _stub.RespondAsync = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var opening = Source.OpenSectorsAsync(Ct).AsTask();

        _stub.Clock.Advance(MasterDataHttp.NseTimeout);
        await using var download = await opening;

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
        download.Failure.ShouldBe("NSE's sector list did not answer in time.");
    }

    [Fact]
    public async Task The_dhan_deadline_is_longer_than_the_nse_one()
    {
        _stub.RespondAsync = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var opening = Source.OpenMasterAsync(null, Ct).AsTask();

        _stub.Clock.Advance(MasterDataHttp.NseTimeout);
        opening.IsCompleted.ShouldBeFalse();
        _stub.Clock.Advance(MasterDataHttp.DhanTimeout - MasterDataHttp.NseTimeout);
        await using var download = await opening;

        download.Failure.ShouldBe("Dhan's instrument file did not answer in time.");
    }

    [Fact]
    public async Task An_unexpected_exception_is_let_out_for_the_caller_to_judge_and_leaves_no_timer_behind()
    {
        _stub.Respond = _ => throw new MasterDataEndpointNotAllowedException("not on the allow-list");

        await Should.ThrowAsync<MasterDataEndpointNotAllowedException>(async () => await Source.OpenSectorsAsync(Ct));

        _stub.Clock.Advance(TimeSpan.FromMinutes(5)); // the released deadline timer cannot fire into the finished call
    }

    [Fact]
    public void Both_clients_leave_the_http_client_timeout_open_because_the_download_deadline_covers_it()
    {
        using var dhan = new HttpClient();
        using var nse = new HttpClient();

        MasterDataHttp.ConfigureDhan(dhan);
        MasterDataHttp.ConfigureNse(nse);

        dhan.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
        nse.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task The_holiday_list_is_one_plain_GET_with_the_query_a_browser_like_agent_and_no_cookie_or_credentials()
    {
        _stub.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"CM\":[]}", System.Text.Encoding.UTF8, "application/json") };

        await using var download = await Source.OpenHolidaysAsync(Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Content);
        (await ReadAllAsync(download)).ShouldBe("{\"CM\":[]}");
        var (method, uri, headers) = _stub.Requests.ShouldHaveSingleItem();
        (method, uri.ToString()).ShouldBe((HttpMethod.Get, "https://www.nseindia.com/api/holiday-master?type=trading"));
        headers["User-Agent"].ShouldBe(MasterDataHttp.BrowserUserAgent);
        headers["Accept"].ShouldStartWith("application/json");
        headers.Keys.ShouldNotContain(k => k == "Authorization" || k == "Cookie" || k == "Proxy-Authorization" || k == "access-token" || k == "client-id");
        headers.ShouldNotContainKey("If-None-Match");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.Found, "302")] // redirects are never followed
    public async Task A_refused_holiday_request_is_a_failure_that_names_the_status_and_the_list(HttpStatusCode status, string code)
    {
        _stub.Respond = _ => new HttpResponseMessage(status);

        await using var download = await Source.OpenHolidaysAsync(Ct);

        download.Kind.ShouldBe(SourceDownloadKind.Failed);
        download.Failure.ShouldBe($"NSE's holiday list answered HTTP {code}.");
    }

    [Fact]
    public async Task A_holiday_list_that_cannot_be_reached_is_reported_without_the_address()
    {
        _stub.Respond = _ => throw new HttpRequestException("connection to https://www.nseindia.com/api/holiday-master?type=trading failed");

        await using var download = await Source.OpenHolidaysAsync(Ct);

        download.Failure.ShouldBe("NSE's holiday list could not be reached.");
    }

    [Fact]
    public async Task A_holiday_list_over_the_cap_is_refused_without_reading()
    {
        _stub.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") };
            response.Content.Headers.ContentLength = MasterDataHttp.HolidayMaxBytes + 1;
            return response;
        };

        await using var download = await Source.OpenHolidaysAsync(Ct);

        download.Failure.ShouldBe("NSE's holiday list is larger than the allowed 1 MB.");
    }

    [Fact]
    public async Task A_holiday_server_that_never_answers_ends_with_a_failure_at_its_deadline()
    {
        _stub.RespondAsync = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        var opening = Source.OpenHolidaysAsync(Ct).AsTask();

        _stub.Clock.Advance(MasterDataHttp.HolidayTimeout);
        await using var download = await opening;

        download.Failure.ShouldBe("NSE's holiday list did not answer in time.");
    }

    [Fact]
    public void The_holiday_client_leaves_the_http_client_timeout_open_and_asks_for_json()
    {
        using var client = new HttpClient();

        MasterDataHttp.ConfigureNseApi(client);

        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldBe(MasterDataHttp.BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.ToString().ShouldStartWith("application/json");
    }

    [Fact]
    public void The_options_accept_a_24_hour_clock_time_only()
    {
        InstrumentRefreshOptions.IsValidTime("08:45").ShouldBeTrue();
        InstrumentRefreshOptions.IsValidTime("00:00").ShouldBeTrue();
        InstrumentRefreshOptions.IsValidTime("23:59").ShouldBeTrue();
        InstrumentRefreshOptions.IsValidTime("24:00").ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime("8:45").ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime("08:45:30").ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime("8.45 am").ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime("").ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime(null).ShouldBeFalse();
        InstrumentRefreshOptions.IsValidTime(new InstrumentRefreshOptions().TimeIst).ShouldBeTrue();
        new InstrumentRefreshOptions().Enabled.ShouldBeTrue();
    }

    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
