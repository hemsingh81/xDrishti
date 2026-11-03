#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Ai;
using XDrishti.Application.Filings;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-08 criteria 5 and 10: the extraction verb over a scripted flagger. Switch off or model not ready refuses before any call; one request at a time; resumable; a second run does nothing; a server failure is recorded and stops; the log carries ids, states and counts only. No model is called.</summary>
public sealed class ExtractAnnouncementsHandlerTests
{
    private static readonly string Secret = "SECRET-ANNOUNCEMENT-TEXT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public AssistantSettings Settings { get; set; } = new(7, true, false, 6);

        public EvalModelInfo Model { get; set; } = new(true, LlmState.Ready, "Qwen3.gguf", new string('a', 64), "b1");

        public bool Nightly { get; set; }

        public TurnGate Gate { get; } = new();

        public FakeFlagger Flagger { get; } = new();

        public MemoryExtractionStore Store { get; } = new();

        public FakeAnnouncementCandidates Candidates { get; } = new();

        public ListLogger Log { get; } = new();

        public Rig Add(int count, DateTimeOffset? at = null)
        {
            for (var i = 0; i < count; i++)
            {
                var id = new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
                Store.Announcements.Add((id, (at ?? new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero)).AddMinutes(i)));
                Candidates.Bodies[id] = new AnnouncementBody("Order", "Subject " + i, Secret + " " + i);
            }

            return this;
        }

        public ExtractAnnouncementsHandler Handler()
        {
            var settings = Substitute.For<IAssistantSettingsStore>();
            settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Settings));
            var environment = Substitute.For<IEvalEnvironment>();
            environment.ModelAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Model));
            var nightly = Substitute.For<INightlyRunProbe>();
            nightly.IsRunningAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Nightly));
            return new ExtractAnnouncementsHandler(settings, environment, nightly, Store, Candidates, Flagger, new TurnOptions(), Gate, new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)), Log);
        }
    }

    private static ExtractAnnouncementsCommand Range(int? limit = null) => new(new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 24), limit);

    [Fact]
    public async Task Each_announcement_in_the_range_gets_one_row_with_the_hashes_and_the_counts_are_returned()
    {
        var rig = new Rig().Add(4);
        rig.Flagger.Script = (_, n) => n switch { 0 => FakeFlagger.Ok(true, "order_win", 4_500_000m), 1 => FakeFlagger.Ok(false), 2 => FakeFlagger.Invalid(ExtractionStates.BadKeys), _ => FakeFlagger.Ok(true, "results", null, true) };

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Attempted, result.Value.Ok, result.Value.InvalidOutput, result.Value.Ungrounded, result.Value.StoppedBecause).ShouldBe((4, 3, 1, 1, null));
        result.Value.MeanLatencyMs.ShouldBe(10);
        rig.Store.Rows.Count.ShouldBe(4);
        rig.Store.Rows.ShouldAllBe(r => r.Key.ModelSha256 == new string('a', 64) && r.Key.PromptHash == PromptRegistry.AnnouncementFlag.Sha256 && r.Key.PromptName == "announcement-flag" && r.Key.SchemaVersion == "1");
        rig.Store.Rows[2].ShouldSatisfyAllConditions(r => r.State.ShouldBe("invalid_output"), r => r.Reason.ShouldBe("bad_keys"), r => r.Answer.ShouldBeNull());
        rig.Flagger.Calls[0].ShouldBe(new AnnouncementFlagInput("Subject 0", "Order", Secret + " 0"));
    }

    [Fact]
    public async Task A_second_run_with_nothing_new_asks_the_model_nothing_and_stores_nothing()
    {
        var rig = new Rig().Add(3);
        await rig.Handler().HandleAsync(Range(), Ct);
        var calls = rig.Flagger.Calls.Count;

        var again = await rig.Handler().HandleAsync(Range(), Ct);

        again.Value.Attempted.ShouldBe(0);
        rig.Flagger.Calls.Count.ShouldBe(calls);
        rig.Store.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_limit_stops_after_that_many_calls_and_the_next_run_resumes_where_it_stopped()
    {
        var rig = new Rig().Add(30);

        var first = await rig.Handler().HandleAsync(Range(limit: 7), Ct);
        var rest = await rig.Handler().HandleAsync(Range(), Ct);

        first.Value.Attempted.ShouldBe(7);
        rest.Value.Attempted.ShouldBe(23);
        rig.Store.Rows.Select(r => r.FilingId).Distinct().Count().ShouldBe(30);
    }

    [Fact]
    public async Task A_changed_model_or_prompt_extracts_again_because_the_key_differs()
    {
        var rig = new Rig().Add(2);
        await rig.Handler().HandleAsync(Range(), Ct);
        rig.Model = rig.Model with { Sha256 = new string('b', 64) };

        var again = await rig.Handler().HandleAsync(Range(), Ct);

        again.Value.Attempted.ShouldBe(2);
        rig.Store.Rows.Select(r => r.Key.ModelSha256).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task The_switch_off_refuses_with_the_existing_code_before_any_call_and_never_turns_it_on()
    {
        var rig = new Rig().Add(2);
        rig.Settings = rig.Settings with { Enabled = false };

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.Error.Code.ShouldBe(ToolErrorCodes.Disabled);
        result.Error.Code.ShouldBe("assistant.disabled");
        rig.Flagger.Calls.ShouldBeEmpty();
        rig.Store.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(true, "abc")]
    [InlineData(true, "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task A_model_that_is_not_ready_or_reports_no_file_hash_refuses_before_any_call(bool known, string sha)
    {
        var rig = new Rig().Add(2);
        rig.Model = rig.Model with { Known = known, Sha256 = sha };

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.Error.Code.ShouldBe("assistant.model_unavailable");
        rig.Flagger.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_switch_going_off_during_a_long_run_stops_it_at_the_next_batch_of_25()
    {
        var rig = new Rig().Add(60);
        rig.Flagger.Script = (_, n) =>
        {
            if (n == 5)
            {
                rig.Settings = rig.Settings with { Enabled = false };
            }

            return FakeFlagger.Ok(false);
        };

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.Value.StoppedBecause.ShouldNotBeNull().ShouldContain("assistant.disabled");
        result.Value.Attempted.ShouldBe(25);
        rig.Store.Rows.Count.ShouldBe(25);
    }

    [Fact]
    public async Task An_upper_case_model_hash_is_stored_in_lower_case_so_the_key_is_one_key()
    {
        var rig = new Rig().Add(1);
        rig.Model = rig.Model with { Sha256 = new string('A', 64) };

        await rig.Handler().HandleAsync(Range(), Ct);

        rig.Store.Rows.Single().Key.ModelSha256.ShouldBe(new string('a', 64));
    }

    [Fact]
    public async Task The_nightly_run_or_a_bad_range_refuses_before_any_call()
    {
        var rig = new Rig().Add(2);
        rig.Nightly = true;

        var nightly = await rig.Handler().HandleAsync(Range(), Ct);
        rig.Nightly = false;
        var backwards = await rig.Handler().HandleAsync(new ExtractAnnouncementsCommand(new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 24), null), Ct);
        var zero = await rig.Handler().HandleAsync(Range(limit: 0), Ct);

        nightly.Error.Code.ShouldBe("assistant.nightly_running");
        backwards.Error.Code.ShouldBe("filings.invalid_query");
        zero.Error.Code.ShouldBe("filings.invalid_query");
        rig.Flagger.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_server_failure_is_recorded_as_model_unavailable_for_that_row_and_the_run_stops()
    {
        var rig = new Rig().Add(5);
        rig.Flagger.DownAfter = 2;

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.StoppedBecause.ShouldNotBeNull().ShouldContain("model_unavailable");
        (result.Value.Attempted, result.Value.Ok, result.Value.ModelUnavailable).ShouldBe((3, 2, 1));
        rig.Store.Rows.Select(r => r.State).ShouldBe(["ok", "ok", "model_unavailable"]);
        rig.Store.Rows[2].Reason.ShouldBe("server_failed");
    }

    [Fact]
    public async Task One_request_at_a_time_a_busy_model_stops_the_run_and_stores_nothing()
    {
        var rig = new Rig().Add(3);
        using var taken = rig.Gate.TryEnter();

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        result.Value.StoppedBecause.ShouldNotBeNull().ShouldContain("busy");
        rig.Flagger.Calls.ShouldBeEmpty();
        rig.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_announcement_whose_stored_text_cannot_be_read_stores_nothing_is_counted_and_does_not_loop()
    {
        var rig = new Rig().Add(4);
        rig.Candidates.Bodies.Remove(rig.Store.Announcements[0].Filing);
        rig.Candidates.Bodies.Remove(rig.Store.Announcements[2].Filing);

        var result = await rig.Handler().HandleAsync(Range(), Ct);

        (result.Value.Attempted, result.Value.NoText).ShouldBe((2, 2));
        rig.Store.Rows.Select(r => r.FilingId).ShouldBe([rig.Store.Announcements[1].Filing, rig.Store.Announcements[3].Filing]);
    }

    [Fact]
    public async Task The_range_is_in_india_dates_and_includes_both_ends()
    {
        var rig = new Rig();
        var id = Guid.NewGuid();
        rig.Store.Announcements.Add((id, new DateTimeOffset(2026, 7, 23, 18, 31, 0, TimeSpan.Zero))); // 2026-07-24 00:01 IST
        var before = Guid.NewGuid();
        rig.Store.Announcements.Add((before, new DateTimeOffset(2026, 7, 23, 18, 29, 0, TimeSpan.Zero))); // 2026-07-23 23:59 IST
        rig.Candidates.Bodies[id] = new AnnouncementBody("Order", "S", "T");
        rig.Candidates.Bodies[before] = new AnnouncementBody("Order", "S", "T");

        await rig.Handler().HandleAsync(Range(), Ct);

        rig.Store.Rows.Select(r => r.FilingId).ShouldBe([id]);
    }

    [Fact]
    public async Task The_log_carries_ids_states_and_counts_and_never_the_text_the_subject_or_the_reason()
    {
        var rig = new Rig().Add(2);
        rig.Flagger.Script = (_, n) => n == 0 ? FakeFlagger.Ok(true, "order_win") : FakeFlagger.Invalid();

        await rig.Handler().HandleAsync(Range(), Ct);

        rig.Log.Messages.ShouldNotBeEmpty();
        rig.Log.Messages.ShouldAllBe(m => m.StartsWith("filings.extract.", StringComparison.Ordinal));
        rig.Log.Messages.ShouldNotContain(m => m.Contains(Secret, StringComparison.Ordinal) || m.Contains("Subject", StringComparison.Ordinal) || m.Contains("a short reason", StringComparison.Ordinal));
        rig.Log.Messages.ShouldContain(m => m.StartsWith("filings.extract.stored", StringComparison.Ordinal) && m.Contains("state=invalid_output", StringComparison.Ordinal) && m.Contains("reason=not_json", StringComparison.Ordinal));
    }

    private sealed class ListLogger : ILogger<ExtractAnnouncementsHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
