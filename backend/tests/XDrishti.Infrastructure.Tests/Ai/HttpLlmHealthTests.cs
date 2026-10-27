using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Infrastructure.Ai.Llm;

namespace XDrishti.Infrastructure.Tests.Ai;

/// <summary>Spec P9-01 AC 6 and 7 over a fake llama.cpp server: each state, the fixed vocabulary, the cache, the switched-off flag, the pinned-file hash.</summary>
public sealed class HttpLlmHealthTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 5, 0, 0, TimeSpan.Zero));

    private HttpLlmHealth Make(FakeLlmServer server, Action<LlmOptions>? change = null)
    {
        var options = new LlmOptions { Enabled = true, ApiKey = FakeLlmServer.Key, HealthTimeoutSeconds = 1 };
        change?.Invoke(options);
        return new HttpLlmHealth(server, Options.Create(options), _time, NullLogger<HttpLlmHealth>.Instance);
    }

    [Fact]
    public async Task A_ready_server_with_the_expected_alias_is_ready_and_reports_file_hash_and_build()
    {
        var status = await Make(FakeLlmServer.Healthy()).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Ready);
        status.Model.ShouldBe("xd-main");
        status.ModelFile.ShouldBe("Qwen3-30B-A3B-Q4_K_M.gguf");
        status.ModelSha256.ShouldBe("0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5");
        status.Build.ShouldBe("b11530-abc");
        status.Detail.ShouldBe("The model server is ready.");
    }

    [Fact]
    public async Task An_unknown_model_file_has_no_hash()
    {
        var status = await Make(FakeLlmServer.Healthy(modelPath: "/opt/m/unknown.gguf")).GetStatusAsync(TestContext.Current.CancellationToken);

        status.ModelFile.ShouldBe("unknown.gguf");
        status.ModelSha256.ShouldBeNull();
    }

    [Fact]
    public async Task A_503_on_health_is_loading_and_the_key_is_not_sent_to_health()
    {
        var server = new FakeLlmServer { Handler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) };

        var status = await Make(server).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Loading);
        server.Requests.ShouldAllBe(r => r.Path == "/health" && r.Authorization == null);
    }

    [Fact]
    public async Task A_refused_connection_is_down_and_the_detail_has_neither_exception_text_nor_address_nor_key()
    {
        var server = new FakeLlmServer { Handler = _ => throw new HttpRequestException("Connection refused (xd-llm:8080) token " + FakeLlmServer.Key) };

        var status = await Make(server).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Down);
        status.Detail.ShouldNotContain("xd-llm");
        status.Detail.ShouldNotContain("8080");
        status.Detail.ShouldNotContain(FakeLlmServer.Key);
        status.Detail.ShouldContain("scripts/xd-ai.sh start");
    }

    [Fact]
    public async Task No_answer_within_the_timeout_is_down()
    {
        var server = new FakeLlmServer { Handler = async request => { await Task.Delay(Timeout.Infinite, TestContext.Current.CancellationToken).ConfigureAwait(false); return new HttpResponseMessage(); } };

        var status = await Make(server).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Down);
    }

    [Fact]
    public async Task A_caller_that_cancels_gets_the_cancellation_not_a_status()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Make(FakeLlmServer.Healthy()).GetStatusAsync(cancel.Token));
    }

    [Fact]
    public async Task A_rejected_key_is_misconfigured()
    {
        var status = await Make(FakeLlmServer.Healthy(), o => o.ApiKey = "wrong-key").GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Misconfigured);
        status.Detail.ShouldBe("The API key was rejected.");
        status.Detail.ShouldNotContain("wrong-key");
    }

    [Fact]
    public async Task A_server_that_serves_another_alias_is_misconfigured()
    {
        var status = await Make(FakeLlmServer.Healthy(alias: "something-else")).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Misconfigured);
        status.Detail.ShouldBe("The server serves another model than the one expected.");
    }

    [Fact]
    public async Task An_empty_key_is_misconfigured_without_any_request()
    {
        var server = FakeLlmServer.Healthy();

        var status = await Make(server, o => o.ApiKey = string.Empty).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Misconfigured);
        server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_garbage_answer_is_not_understood_and_a_redirect_is_not_followed()
    {
        var garbage = new FakeLlmServer { Handler = request => Task.FromResult(request.RequestUri!.AbsolutePath == "/health" ? FakeLlmServer.Json(HttpStatusCode.OK, "{}") : FakeLlmServer.Json(HttpStatusCode.OK, "<html>")) };
        (await Make(garbage).GetStatusAsync(TestContext.Current.CancellationToken)).State.ShouldBe(LlmState.Down);

        var redirect = new FakeLlmServer { Handler = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)) };
        (await Make(redirect).GetStatusAsync(TestContext.Current.CancellationToken)).State.ShouldBe(LlmState.Down);
    }

    [Fact]
    public async Task The_answer_is_cached_for_the_configured_seconds()
    {
        var server = FakeLlmServer.Healthy();
        var health = Make(server);

        await health.GetStatusAsync(TestContext.Current.CancellationToken);
        var calls = server.Requests.Count;
        _time.Advance(TimeSpan.FromSeconds(4));
        await health.GetStatusAsync(TestContext.Current.CancellationToken);
        server.Requests.Count.ShouldBe(calls);

        _time.Advance(TimeSpan.FromSeconds(2));
        await health.GetStatusAsync(TestContext.Current.CancellationToken);
        server.Requests.Count.ShouldBe(calls * 2);
    }

    [Fact]
    public async Task While_the_deployment_flag_is_off_nothing_is_requested()
    {
        var server = FakeLlmServer.Healthy();

        var status = await Make(server, o => o.Enabled = false).GetStatusAsync(TestContext.Current.CancellationToken);

        status.State.ShouldBe(LlmState.Down);
        status.Detail.ShouldBe("The assistant's model server is switched off in this deployment.");
        server.Requests.ShouldBeEmpty();
    }
}
