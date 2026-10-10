using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace XDrishti.LlmBench.Tests;

public class SseParserTests
{
    private static StreamChunk Parse(string line)
    {
        SseParser.TryParseLine(line, out var chunk, out _).ShouldBeTrue();
        return chunk!;
    }

    [Fact]
    public void A_content_delta_is_parsed()
    {
        Parse("data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}]}").Content.ShouldBe("Hel");
    }

    [Fact]
    public void Reasoning_deltas_count_as_text_so_thinking_models_have_a_first_token_time()
    {
        var chunk = Parse("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"hmm\"}}]}");

        chunk.Reasoning.ShouldBe("hmm");
        chunk.HasText.ShouldBeTrue();
    }

    [Fact]
    public void The_final_chunk_carries_llama_cpp_timings_and_usage()
    {
        var chunk = Parse("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1000,\"completion_tokens\":200},\"timings\":{\"prompt_n\":1000,\"prompt_ms\":500.0,\"prompt_per_second\":2000.0,\"predicted_n\":200,\"predicted_ms\":10000.0,\"predicted_per_second\":20.0}}");

        chunk.FinishReason.ShouldBe("stop");
        chunk.PromptTokens.ShouldBe(1000);
        chunk.Timings!.PredictedPerSecond.ShouldBe(20.0);
        chunk.HasText.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(": keep-alive")]
    public void Blank_lines_and_comments_are_skipped(string line)
    {
        SseParser.TryParseLine(line, out var chunk, out var done).ShouldBeFalse();
        chunk.ShouldBeNull();
        done.ShouldBeFalse();
    }

    [Fact]
    public void The_done_marker_ends_the_stream()
    {
        SseParser.TryParseLine("data: [DONE]", out _, out var done).ShouldBeFalse();
        done.ShouldBeTrue();
    }

    [Fact]
    public void An_error_object_inside_the_stream_fails_the_run_instead_of_looking_like_an_empty_answer()
    {
        Should.Throw<HttpRequestException>(() => SseParser.TryParseLine("data: {\"error\":{\"message\":\"context overflow\"}}", out _, out _));
    }

    [Fact]
    public void A_non_sse_line_is_an_error()
    {
        Should.Throw<FormatException>(() => SseParser.TryParseLine("{\"error\":1}", out _, out _));
    }
}

public class ChatClientTests
{
    /// <summary>A response body whose reads advance a fake clock, so time-to-first-token is deterministic.</summary>
    private sealed class SteppingStream(string text, FakeTimeProvider clock, TimeSpan step) : Stream
    {
        private readonly MemoryStream _inner = new(Encoding.UTF8.GetBytes(text));

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            clock.Advance(step);
            return _inner.Read(buffer, offset, Math.Min(count, 40));
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private const string Stream =
        "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n"
        + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"timings\":{\"prompt_n\":50,\"prompt_ms\":100,\"prompt_per_second\":500,\"predicted_n\":2,\"predicted_ms\":200,\"predicted_per_second\":10}}\n\ndata: [DONE]\n\n";

    [Fact]
    public async Task A_stream_yields_text_server_timings_and_a_time_to_first_token()
    {
        var clock = new FakeTimeProvider();
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SteppingStream(Stream, clock, TimeSpan.FromMilliseconds(50))) });
        using var http = new HttpClient(handler);
        var client = new ChatClient(http, "http://127.0.0.1:8080/", new ChatSettings("m", false), clock);

        var run = await client.StreamAsync(string.Empty, "hi", 16, TestContext.Current.CancellationToken);

        run.Text.ShouldBe("Hello world");
        run.PromptTokens.ShouldBe(50);
        run.CompletionTokens.ShouldBe(2);
        run.GenTps.ShouldBe(10);
        run.PromptTps.ShouldBe(500);
        run.TtftMs.ShouldBeGreaterThan(0);
        run.TotalMs.ShouldBeGreaterThanOrEqualTo(run.TtftMs);
    }

    [Fact]
    public void Without_server_timings_speed_is_derived_from_the_stream()
    {
        var run = new RunResult(1000, 3000, 100, 41, null, null, "x");

        run.GenTps.ShouldBe(20);
    }

    [Fact]
    public void The_answer_excludes_reasoning_tokens_so_a_needle_quoted_in_reasoning_does_not_count()
    {
        var run = new RunResult(1, 2, 3, 4, null, null, "the code might be 4417", "I am not sure");

        run.Text.ShouldContain("4417");
        run.Answer.ShouldNotContain("4417");
    }

    [Fact]
    public async Task A_server_error_is_reported_with_its_body()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("loading model") });
        using var http = new HttpClient(handler);
        var client = new ChatClient(http, "http://127.0.0.1:8080", new ChatSettings("m", false), TimeProvider.System);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.StreamAsync(string.Empty, "hi", 16, TestContext.Current.CancellationToken));

        ex.Message.ShouldContain("503");
        ex.Message.ShouldContain("loading model");
    }

    [Fact]
    public void The_request_defeats_prompt_caching_is_deterministic_and_can_disable_thinking()
    {
        using var http = new HttpClient();
        var client = new ChatClient(http, "http://x", new ChatSettings("m", DisableThinking: true), TimeProvider.System);

        var body = client.BuildRequest("sys", "user", 100, stream: true);

        body.ShouldContain("\"cache_prompt\":false");
        body.ShouldContain("\"temperature\":0");
        body.ShouldContain("\"seed\":1");
        body.ShouldContain("\"enable_thinking\":false");
        body.ShouldContain("\"include_usage\":true");
    }
}

public class ScenarioTests
{
    [Theory]
    [InlineData(1000, 4.0)]
    [InlineData(8000, 2.6)]
    public void Prompts_are_deterministic_and_sized_by_the_measured_characters_per_token(int tokens, double charsPerToken)
    {
        var first = PromptFactory.Filler(tokens, "note", charsPerToken);

        first.ShouldBe(PromptFactory.Filler(tokens, "note", charsPerToken));
        first.Length.ShouldBe((int)(tokens * charsPerToken));
    }

    [Fact]
    public async Task Calibration_uses_the_servers_tokenizer_to_hit_the_requested_prompt_size()
    {
        // A tokenizer that sees 2 characters per token: the filler for "1,000 tokens" must then be 2,000 characters.
        var handler = new SimpleHandler(request => request.RequestUri!.AbsolutePath == "/tokenize"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tokens\":[" + string.Join(",", Enumerable.Range(0, 2000)) + "]}") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var client = new ChatClient(http, "http://x", new ChatSettings("m", false), TimeProvider.System);
        var messages = new List<string>();
        var scenarios = new Scenarios(client, 1, 8, messages.Add);

        await scenarios.CalibrateAsync(TestContext.Current.CancellationToken);

        messages.ShouldContain(m => m.Contains("2.00 characters per token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_server_without_a_tokenizer_endpoint_has_no_token_count()
    {
        using var http = new HttpClient(new SimpleHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new ChatClient(http, "http://x", new ChatSettings("m", false), TimeProvider.System);

        (await client.CountTokensAsync("hello", TestContext.Current.CancellationToken)).ShouldBeNull(); // Scenarios then assumes 4 characters per token (logged)
    }

    private sealed class SimpleHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    [Fact]
    public void The_needle_is_inside_the_long_prompt()
    {
        PromptFactory.Needle(30000, "4417").ShouldContain("the vault code is 4417");
    }

    [Theory]
    [InlineData("{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"get_plan\",\"arguments\":\"{\\\"date\\\":\\\"2026-10-09\\\",\\\"account\\\":\\\"main\\\"}\"}}]}}]}", true)]
    [InlineData("{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"get_plan\",\"arguments\":{\"date\":\"2026-10-09\",\"account\":\"main\"}}}]}}]}", true)]
    [InlineData("{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"get_plan\",\"arguments\":\"{\\\"date\\\":\\\"2026-10-09\\\"}\"}}]}}]}", false)]
    [InlineData("{\"choices\":[{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"other\",\"arguments\":\"{}\"}}]}}]}", false)]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"I cannot\"}}]}", false)]
    [InlineData("not json", false)]
    public void A_tool_call_is_valid_only_with_the_right_name_and_both_required_arguments(string json, bool expected)
    {
        ToolSchema.IsValidToolCall(json).ShouldBe(expected);
    }

    [Fact]
    public void Median_handles_odd_even_and_empty()
    {
        Stats.Median([3, 1, 2]).ShouldBe(2);
        Stats.Median([4, 1, 2, 3]).ShouldBe(2.5);
        Stats.Median([]).ShouldBe(0);
    }
}

public class ReportTests
{
    private static RunResult Run(double ttft, double total, double tps) => new(ttft, total, 8000, 200, 800, tps, "x");

    private static BenchResults Results(double genTps, double briefingTtftMs = 8000, double loadMs = 20000)
        => new(
            "q30 / container-gpu",
            "Qwen3-30B-A3B",
            new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.FromMinutes(330)),
            [new ThroughputResult(8192, [Run(5000, 15000, genTps), Run(5200, 15500, genTps)])],
            new ThroughputResult(3500, [Run(briefingTtftMs, 30000, genTps)]),
            [new SmokeResult("tool calls valid", true, "10/10")],
            loadMs);

    [Fact]
    public void A_fast_configuration_passes_every_threshold()
    {
        var checks = new Thresholds().Evaluate(Results(genTps: 40));

        checks.ShouldAllBe(c => c.Pass == true);
    }

    [Fact]
    public void A_slow_configuration_fails_the_speed_and_first_token_thresholds()
    {
        var checks = new Thresholds().Evaluate(Results(genTps: 9, briefingTtftMs: 20000));

        checks.Single(c => c.Measure.StartsWith("Generation", StringComparison.Ordinal)).Pass.ShouldBe(false);
        checks.Single(c => c.Measure.StartsWith("Time-to-first-token", StringComparison.Ordinal)).Pass.ShouldBe(false);
    }

    [Theory]
    [InlineData(15.0, true)]
    [InlineData(14.9, false)]
    public void The_speed_threshold_is_inclusive_at_the_boundary(double tps, bool expected)
    {
        new Thresholds().Evaluate(Results(genTps: tps)).Single(c => c.Measure.StartsWith("Generation", StringComparison.Ordinal)).Pass.ShouldBe(expected);
    }

    [Fact]
    public void A_slow_load_fails_the_load_threshold_and_a_failed_tool_smoke_fails_that_row()
    {
        var slowLoad = new Thresholds().Evaluate(Results(genTps: 40, loadMs: 61250));
        var badTools = Results(genTps: 40) with { Smoke = [new SmokeResult("tool calls valid", false, "6/10")] };

        slowLoad.Single(c => c.Measure.StartsWith("Load", StringComparison.Ordinal)).Pass.ShouldBe(false);
        new Thresholds().Evaluate(badTools).Single(c => c.Measure.StartsWith("Tool", StringComparison.Ordinal)).Pass.ShouldBe(false);
    }

    [Fact]
    public void Missing_measurements_are_not_reported_as_pass_or_fail()
    {
        var empty = new BenchResults("x", "m", DateTimeOffset.UnixEpoch, [], null, [], null);

        new Thresholds().Evaluate(empty).ShouldAllBe(c => c.Pass == null);
    }

    [Fact]
    public void The_report_shows_throughput_smoke_and_thresholds()
    {
        var report = ReportBuilder.Render(Results(genTps: 40), new Thresholds());

        report.ShouldContain("# LLM benchmark — q30 / container-gpu");
        report.ShouldContain("| 8,192 |");
        report.ShouldContain("briefing");
        report.ShouldContain("tool calls valid");
        report.ShouldContain("## Thresholds");
    }
}
