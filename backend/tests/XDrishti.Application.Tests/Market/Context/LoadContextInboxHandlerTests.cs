using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Tests.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>P3-01d fallback D at the level of the use case: what is loaded, the precedence of stored days, idempotence, and where files go.</summary>
public sealed class LoadContextInboxHandlerTests
{
    private const string Nifty = "Date,Open,High,Low,Close\n02-Mar-2026,1,110,90,100\n03-Mar-2026,1,120,95,110\n07-Mar-2026,1,120,95,110\n";

    private const string Vix = "Date,Open,High,Low,Close,Prev. Close,Change,% Change\n02-Mar-2026,15,16,14,15.5,15,0.5,3\n03-Mar-2026,15,16,14,14.5,15.5,-1,-6\n";

    private sealed class FakeInbox(Dictionary<string, string> files) : IContextInbox
    {
        public bool Enabled { get; set; } = true;

        public Dictionary<string, bool> Moved { get; } = [];

        public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>>([.. files.Keys.Where(k => !Moved.ContainsKey(k)).Order()]);

        public ValueTask<string?> ReadAsync(string name, CancellationToken cancellationToken) => ValueTask.FromResult(files.GetValueOrDefault(name));

        public ValueTask MoveAsync(string name, bool processed, CancellationToken cancellationToken)
        {
            Moved[name] = processed;
            return ValueTask.CompletedTask;
        }
    }

    private readonly FakeContextDayStore _store = new();

    private readonly FakeContextSettings _settings = new() { VixGraceSessions = 2 };

    // Monday 2026-03-09 16:30 India time: the last closed session is that Monday.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 9, 16, 30, 0, TimeSpan.FromMinutes(330)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LoadContextInboxHandler Handler(FakeInbox inbox) => new(_settings, inbox, _store, new FixedCalendar(), _clock, NullLogger<LoadContextInboxHandler>.Instance);

    [Fact]
    public async Task Index_days_and_vix_closes_are_stored_with_the_csv_source_and_the_weekend_row_is_refused()
    {
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = Nifty, ["vix.csv"] = Vix });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        (done.Files, done.FilesFailed, done.IndexWritten, done.VixWritten, done.AlreadyStored).ShouldBe((2, 0, 2, 2, 0));
        done.Rejected.ShouldBe(new Dictionary<string, int> { [ContextCsvCodes.NotASession] = 1 });
        _store.Sources.Values.ShouldAllBe(s => s == "nse_csv");
        _store.Rows.Select(r => r.Date).Order().ShouldBe([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)]);
        inbox.Moved.ShouldBe(new Dictionary<string, bool> { ["nifty50.csv"] = true, ["vix.csv"] = true });
    }

    [Fact]
    public async Task A_stored_day_is_never_overwritten_and_a_stored_vix_is_never_replaced()
    {
        _store.Rows.Add(new ContextDayRecord(new DateOnly(2026, 3, 2), 999, 900, 950, 20, 375, 375));
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = Nifty, ["vix.csv"] = Vix });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        (done.IndexWritten, done.VixWritten, done.AlreadyStored).ShouldBe((1, 1, 2));
        _store.Rows.Single(r => r.Date == new DateOnly(2026, 3, 2)).IndexClose.ShouldBe(950);
        _store.VixCsv.Select(v => v.Date).ShouldBe([new DateOnly(2026, 3, 3)]);
    }

    [Fact]
    public async Task Loading_the_same_files_again_writes_nothing()
    {
        await Handler(new FakeInbox(new() { ["nifty50.csv"] = Nifty, ["vix.csv"] = Vix })).HandleAsync(new LoadContextInboxCommand(), Ct);

        var again = (await Handler(new FakeInbox(new() { ["nifty50.csv"] = Nifty, ["vix.csv"] = Vix })).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        (again.IndexWritten, again.VixWritten, again.AlreadyStored).ShouldBe((0, 0, 4));
        _store.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_day_after_the_last_closed_session_is_not_final_and_waits()
    {
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = "Date,Open,High,Low,Close\n10-Mar-2026,1,110,90,100\n" });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        done.IndexWritten.ShouldBe(0);
        done.Rejected[ContextCsvCodes.NotFinal].ShouldBe(1);
    }

    [Fact]
    public async Task An_index_day_inside_the_vix_grace_is_held_back_and_its_file_stays_in_the_inbox()
    {
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = "Date,Open,High,Low,Close\n06-Mar-2026,1,110,90,100\n" });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        done.IndexWritten.ShouldBe(0);
        done.Rejected[ContextCsvCodes.NotFinal].ShouldBe(1);
        inbox.Moved.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_whose_rows_are_all_refused_goes_to_failed()
    {
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = "Date,Open,High,Low,Close\n07-Mar-2026,1,110,90,100\n" });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        (done.FilesFailed, done.IndexWritten).ShouldBe((1, 0));
        inbox.Moved.ShouldBe(new Dictionary<string, bool> { ["nifty50.csv"] = false });
    }

    [Fact]
    public async Task Two_files_with_the_same_date_and_different_values_count_a_conflict_and_keep_the_first()
    {
        var inbox = new FakeInbox(new()
        {
            ["a_nifty50.csv"] = "Date,Open,High,Low,Close\n02-Mar-2026,1,110,90,100\n",
            ["b_nifty50.csv"] = "Date,Open,High,Low,Close\n02-Mar-2026,1,120,90,105\n",
        });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        done.Rejected[ContextCsvCodes.Conflict].ShouldBe(1);
        _store.Rows.Single().IndexClose.ShouldBe(100);
    }

    [Fact]
    public async Task A_disabled_context_reads_nothing()
    {
        _settings.Enabled = false;
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = Nifty });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        done.Files.ShouldBe(0);
        inbox.Moved.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_or_has_no_header_goes_to_failed_and_the_others_still_load()
    {
        var inbox = new FakeInbox(new() { ["bad.csv"] = "hello", ["nifty50.csv"] = Nifty });

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        (done.Files, done.FilesFailed, done.IndexWritten).ShouldBe((2, 1, 2));
        inbox.Moved["bad.csv"].ShouldBeFalse();
        inbox.Moved["nifty50.csv"].ShouldBeTrue();
    }

    [Fact]
    public async Task A_switched_off_loader_reads_nothing()
    {
        var inbox = new FakeInbox(new() { ["nifty50.csv"] = Nifty }) { Enabled = false };

        var done = (await Handler(inbox).HandleAsync(new LoadContextInboxCommand(), Ct)).Value;

        done.Files.ShouldBe(0);
        inbox.Moved.ShouldBeEmpty();
        _store.Rows.ShouldBeEmpty();
    }
}
