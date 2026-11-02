using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>Spec P15-03 criteria 5, 10 and 11: the parse step is idempotent, records pdf as not_xbrl without opening it, saves every outcome in one call per filing, survives a bad file and logs counts and codes only.</summary>
public sealed class ParseResultsHandlerTests
{
    private static readonly DateOnly Q1 = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryStoreOfParses : IResultFactsStore
    {
        public List<PendingResultFiling> Pending { get; } = [];

        public Dictionary<Guid, (string Version, ParsedResults Parsed)> Saved { get; } = [];

        public Exception? ThrowOn { get; set; }

        public ValueTask<IReadOnlyList<PendingResultFiling>> PendingAsync(string parserVersion, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<PendingResultFiling>>([.. Pending.Where(p => !Saved.ContainsKey(p.Id))]);

        public ValueTask<bool> SaveAsync(Guid filingId, string parserVersion, ParsedResults parsed, DateTimeOffset at, CancellationToken cancellationToken)
        {
            if (ThrowOn is not null)
            {
                throw ThrowOn;
            }

            return ValueTask.FromResult(Saved.TryAdd(filingId, (parserVersion, parsed)));
        }
    }

    private sealed class FakeReader : IResultsXbrlReader
    {
        public int Calls { get; private set; }

        public ValueTask<ParsedResults> ParseAsync(Stream file, DateOnly periodEnd, CancellationToken cancellationToken)
        {
            Calls++;
            var text = new StreamReader(file).ReadToEnd();
            return ValueTask.FromResult(text == "bad" ? ParsedResults.Reject("not_xml") : text == "nothing" ? ParsedResults.NoFactsBecause("nothing_mapped", new Dictionary<string, int>()) : new ParsedResults("parsed", null, [ParsedFact.Of("revenue", "q", "consolidated", 1m)], new Dictionary<string, int> { ["unknown_span"] = 2 }));
        }
    }

    private sealed class Recording : ILogger<ParseResultsHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private static PendingResultFiling Pending(int n, string name = "a.xml", DateOnly? period = null) => new(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), period ?? Q1, new string((char)('a' + n), 64), name);

    private static (ParseResultsHandler Handler, MemoryStoreOfParses Store, FakeFiles Files, FakeReader Reader, Recording Log) Build(params PendingResultFiling[] pending)
    {
        var store = new MemoryStoreOfParses();
        store.Pending.AddRange(pending);
        var files = new FakeFiles();
        foreach (var p in pending)
        {
            files.Content[p.ContentSha256] = p.FileName.StartsWith("bad", StringComparison.Ordinal) ? "bad" : p.FileName.StartsWith("nothing", StringComparison.Ordinal) ? "nothing" : "ok";
        }

        var reader = new FakeReader();
        var log = new Recording();
        return (new ParseResultsHandler(store, files, reader, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), log), store, files, reader, log);
    }

    [Fact]
    public async Task Every_pending_file_is_parsed_and_saved_once_with_the_current_parser_version()
    {
        var (handler, store, _, _, _) = Build(Pending(1), Pending(2), Pending(3, "bad.xml"), Pending(4, "nothing.xml"));

        var summary = (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;

        (summary.Pending, summary.Parsed, summary.Rejected, summary.NoFacts, summary.NotXbrl, summary.Failures).ShouldBe((4, 2, 1, 1, 0, 0));
        store.Saved.Count.ShouldBe(4);
        store.Saved.Values.ShouldAllBe(v => v.Version == "xbrl-1-synthetic");
    }

    [Fact]
    public async Task A_second_run_parses_nothing()
    {
        var (handler, _, _, reader, _) = Build(Pending(1), Pending(2));
        await handler.HandleAsync(new ParseResultsCommand(), Ct);

        var second = (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;

        (second.Pending, second.Parsed).ShouldBe((0, 0));
        reader.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData("a.pdf")]
    [InlineData("a.csv")]
    [InlineData("a.txt")]
    public async Task A_file_that_is_not_xml_by_name_is_recorded_not_xbrl_and_never_opened(string name)
    {
        var (handler, store, files, reader, _) = Build(Pending(1, name));
        files.Content.Clear();

        var summary = (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;

        (summary.NotXbrl, summary.Failures).ShouldBe((1, 0));
        reader.Calls.ShouldBe(0);
        store.Saved.Single().Value.Parsed.Outcome.ShouldBe("not_xbrl");
    }

    [Fact]
    public async Task A_file_with_no_period_is_no_facts_with_the_reason_no_period()
    {
        var (handler, store, _, reader, _) = Build(new PendingResultFiling(Guid.NewGuid(), null, new string('a', 64), "a.xml"));

        await handler.HandleAsync(new ParseResultsCommand(), Ct);

        reader.Calls.ShouldBe(0);
        (store.Saved.Single().Value.Parsed.Outcome, store.Saved.Single().Value.Parsed.Reason).ShouldBe(("no_facts", "no_period"));
    }

    [Fact]
    public async Task A_stored_file_that_cannot_be_opened_is_a_failure_left_for_the_next_run_and_stops_no_other_file()
    {
        var (handler, store, files, _, _) = Build(Pending(1), Pending(2));
        files.Content.Remove(Pending(1).ContentSha256);

        var summary = (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;

        (summary.Failures, summary.Parsed).ShouldBe((1, 1));
        store.Saved.Keys.ShouldBe([Pending(2).Id]);
    }

    [Fact]
    public async Task A_store_that_throws_is_a_failure_by_type_and_not_an_exception()
    {
        var (handler, store, _, _, log) = Build(Pending(1));
        store.ThrowOn = new InvalidOperationException("secret detail with C:\\path");

        var summary = (await handler.HandleAsync(new ParseResultsCommand(), Ct)).Value;

        summary.Failures.ShouldBe(1);
        log.Lines.ShouldContain(l => l.Contains("InvalidOperationException", StringComparison.Ordinal));
        log.Lines.ShouldNotContain(l => l.Contains("secret", StringComparison.Ordinal) || l.Contains("C:\\", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_log_carries_counts_ids_and_codes_only()
    {
        var (handler, _, _, _, log) = Build(Pending(1), Pending(3, "bad.xml"));

        await handler.HandleAsync(new ParseResultsCommand(), Ct);

        var text = string.Join("\n", log.Lines);
        text.ShouldContain("filings.parsed");
        text.ShouldContain("filings.parse_rejected");
        text.ShouldContain("reason=not_xml");
        text.ShouldContain("filings.parse_flags");
        text.ShouldContain("unknown_span:2");
        text.ShouldNotContain("a.xml");
        text.ShouldNotContain("bad.xml");
    }

    [Fact]
    public async Task A_cancelled_run_throws_cancellation_and_saves_nothing()
    {
        var (handler, store, _, _, _) = Build(Pending(1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(new ParseResultsCommand(), cancelled.Token));

        store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_facts_handler_answers_not_found_not_parsed_and_the_stored_facts()
    {
        var reader = Substitute.For<IFilingReader>();
        var rows = Substitute.For<IResultFactRows>();
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        reader.FindAsync(id, Arg.Any<CancellationToken>()).Returns(new FilingView(id, "X", "results", Q1, null, "ok", null, DateTimeOffset.UnixEpoch, new string('a', 64), 1, "f.xml", null));
        reader.FindAsync(other, Arg.Any<CancellationToken>()).Returns((FilingView?)null);
        var handler = new GetFilingFactsHandler(reader, rows);

        var missing = await handler.HandleAsync(new GetFilingFactsQuery(other), Ct);
        var notParsed = await handler.HandleAsync(new GetFilingFactsQuery(id), Ct);
        rows.ParseOfAsync(id, "xbrl-1-synthetic", Arg.Any<CancellationToken>()).Returns(new StoredParse(id, "xbrl-1-synthetic", "parsed", null, 1, new Dictionary<string, int> { ["x"] = 1 }));
        rows.FactsOfAsync(id, "xbrl-1-synthetic", Arg.Any<CancellationToken>()).Returns([new StoredResultFact("revenue", "q", "consolidated", 5m, "inr", null)]);
        var parsed = await handler.HandleAsync(new GetFilingFactsQuery(id), Ct);

        missing.Error.Code.ShouldBe("filings.not_found");
        notParsed.Value.Outcome.ShouldBe("not_parsed");
        (parsed.Value.ParserVersion, parsed.Value.Outcome, parsed.Value.Facts.Single().Value, parsed.Value.Flags["x"]).ShouldBe(("xbrl-1-synthetic", "parsed", 5m, 1));
    }

    [Fact]
    public async Task The_as_of_port_reads_up_to_t_minus_the_lag_with_the_current_version()
    {
        var rows = Substitute.For<IResultFactRows>();
        var settings = new FakeSettings();
        var t = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        rows.ForInstrumentAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        var answer = await new ResultFactsAsOf(rows, settings).Facts(Guid.NewGuid(), t, Ct);

        answer.Facts.ShouldBeEmpty();
        await rows.Received(1).ForInstrumentAsync(Arg.Any<Guid>(), t - TimeSpan.FromMinutes(settings.LagMinutes), "xbrl-1-synthetic", Arg.Any<CancellationToken>());
    }
}
