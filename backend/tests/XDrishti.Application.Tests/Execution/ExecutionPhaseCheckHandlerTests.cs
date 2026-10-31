using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Execution;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>Spec P12-12 criteria 2, 4 to 7, 11a, 13 on fakes: the use case reads the records port only, builds the sessions, and answers the three outcomes.</summary>
public sealed class ExecutionPhaseCheckHandlerTests
{
    private static readonly Guid Account = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Promoted = new(2026, 9, 1, 8, 0, 0, TimeSpan.FromMinutes(330));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Ist(int day, int h, int m) => new(2026, 10, day, h, m, 0, TimeSpan.FromMinutes(330));

    private sealed class FixedCalendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private sealed class FakeReader : IExecutionRecordsReader
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<PhaseAccount> Accounts { get; set; } = [];

        public IReadOnlyList<PhaseTrailRow> Trail { get; set; } = [];

        public IReadOnlyList<PhaseSettingsRow> Settings { get; set; } = [];

        public DateOnly? Newest { get; set; }

        public Func<DateOnly, DateOnly, PhaseAccountRecords> Records { get; set; } = (_, _) => Empty;

        public static PhaseAccountRecords Empty { get; } = new([], [], [], [], [], [], [], [], [], 0, 0, 0, []);

        public ValueTask<IReadOnlyList<PhaseAccount>> AccountsAsync(Guid? accountId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(AccountsAsync));
            return ValueTask.FromResult<IReadOnlyList<PhaseAccount>>([.. Accounts.Where(a => accountId is null || a.Id == accountId)]);
        }

        public ValueTask<IReadOnlyList<PhaseTrailRow>> TrailAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(TrailAsync));
            return ValueTask.FromResult(Trail);
        }

        public ValueTask<IReadOnlyList<PhaseSettingsRow>> SettingsAsync(CancellationToken cancellationToken)
        {
            Calls.Add(nameof(SettingsAsync));
            return ValueTask.FromResult(Settings);
        }

        public ValueTask<DateOnly?> NewestSessionAsync(CancellationToken cancellationToken)
        {
            Calls.Add(nameof(NewestSessionAsync));
            return ValueTask.FromResult(Newest);
        }

        public ValueTask<PhaseAccountRecords> RecordsAsync(Guid accountId, DateOnly from, DateOnly through, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(RecordsAsync));
            return ValueTask.FromResult(Records(from, through));
        }
    }

    private static IBrokerAllowList Pinned()
    {
        var list = Substitute.For<IBrokerAllowList>();
        list.Entries().Returns(XDrishti.Domain.Planning.PlanPhaseCheck.PinnedAllowList);
        list.Allows(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        return list;
    }

    private static EvaluateExecutionPhaseCheckHandler Handler(FakeReader reader, DateTimeOffset now) =>
        new(reader, new FixedCalendar(), Pinned(), new FakeTimeProvider(now), NullLogger<EvaluateExecutionPhaseCheckHandler>.Instance);

    private static FakeReader SemiAutoReader(Func<DateOnly, bool>? broken = null, DateTimeOffset? promoted = null)
    {
        var reader = new FakeReader
        {
            Accounts = [new PhaseAccount(Account, "Main")],
            Trail = [new PhaseTrailRow(promoted ?? Promoted, "assisted", "semi_auto", "promotion")],
            Settings = [new PhaseSettingsRow(Promoted.AddDays(-30), false)],
        };
        reader.Records = (from, through) =>
        {
            var days = Enumerable.Range(0, through.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
            return new PhaseAccountRecords(
                [.. days.Select(d => new PhaseEntryRow(d, "live", true))], [], [.. days.SelectMany(d => new[] { new PhaseRunRow(d, "eod", true), new PhaseRunRow(d, "final", true) })],
                [.. days.Where(d => broken?.Invoke(d) == true).Select(d => new PhaseBreakRow(d, "position_mismatch"))], [], [], [.. days.Select(d => new PhaseDayStateRow(d, "ready"))], [], [], 0, 0, 0, []);
        };
        return reader;
    }

    private static async Task<ExecutionPhaseCheckView> RunAsync(FakeReader reader, DateTimeOffset now, DateOnly? through = null)
    {
        var result = await Handler(reader, now).HandleAsync(new EvaluateExecutionPhaseCheckQuery(through, null), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        return result.Value;
    }

    [Fact]
    public async Task An_empty_database_is_not_evaluable_with_the_limits_printed()
    {
        var view = await RunAsync(new FakeReader(), Ist(31, 12, 0));

        view.Report.ExitVerdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        view.Report.Accounts.ShouldBeEmpty();
        view.Text.ShouldContain("honest limits:");
        view.Report.Boundary.Text.ShouldStartWith("MET");
    }

    [Fact]
    public async Task An_account_that_never_reached_semi_auto_has_no_window_and_no_records_are_read()
    {
        var reader = new FakeReader { Accounts = [new PhaseAccount(Account, "Main")], Trail = [new PhaseTrailRow(Promoted, "paper", "manual_live", "promotion")] };

        var view = await RunAsync(reader, Ist(31, 12, 0));

        view.Report.Accounts.Single().Label.ShouldContain("no semi-auto window");
        reader.Calls.ShouldNotContain(nameof(FakeReader.RecordsAsync));
    }

    [Fact]
    public async Task Forty_three_clean_confirmed_sessions_are_met()
    {
        var view = await RunAsync(SemiAutoReader(), Ist(31, 12, 0));

        view.Report.Through.ShouldBe(new DateOnly(2026, 10, 30));
        view.Report.Accounts.Single().Verdict.ShouldBe(PhaseVerdicts.Met);
        view.Report.ExitVerdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public async Task A_break_on_a_day_inside_the_window_leaves_not_met_when_fewer_than_thirty_follow()
    {
        var view = await RunAsync(SemiAutoReader(d => d == new DateOnly(2026, 10, 12)), Ist(31, 12, 0));

        var account = view.Report.Accounts.Single();
        account.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        account.Streak.ShouldBe(14);
        account.LastBreak!.ShouldContain("2026-10-12");
    }

    [Fact]
    public async Task A_canary_pending_default_with_no_settings_row_counts_nothing()
    {
        var reader = SemiAutoReader();
        reader.Settings = [];

        var account = (await RunAsync(reader, Ist(31, 12, 0))).Report.Accounts.Single();

        account.Counted.ShouldBe(0);
        account.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        account.Sessions.ShouldAllBe(s => s.Reasons.Contains("canary_session"));
    }

    [Fact]
    public async Task A_promotion_taking_effect_at_nine_exactly_is_in_mode_and_one_second_later_is_out_of_mode_that_day()
    {
        var at9 = (await RunAsync(SemiAutoReader(promoted: Ist(26, 9, 0)), Ist(31, 12, 0))).Report.Accounts.Single().Sessions;
        var late = (await RunAsync(SemiAutoReader(promoted: Ist(26, 9, 0).AddSeconds(1)), Ist(31, 12, 0))).Report.Accounts.Single().Sessions;

        at9[0].Class.ShouldBe("counted");
        late[0].Class.ShouldBe("out_of_mode");
        late[1].Class.ShouldBe("counted");
    }

    [Fact]
    public async Task An_entry_without_a_confirmed_ticket_breaks_that_session_with_its_reason()
    {
        var reader = SemiAutoReader(promoted: Ist(26, 8, 0));
        var inner = reader.Records;
        reader.Records = (f, t) => inner(f, t) with { Entries = [new PhaseEntryRow(new DateOnly(2026, 10, 27), "live", false)] };

        var sessions = (await RunAsync(reader, Ist(31, 12, 0))).Report.Accounts.Single().Sessions;

        sessions.Single(s => s.Date == new DateOnly(2026, 10, 27)).Reasons.ShouldContain("unconfirmed_entry");
    }

    [Fact]
    public async Task Sandbox_intents_are_listed_and_never_counted()
    {
        var reader = SemiAutoReader(promoted: Ist(26, 8, 0));
        reader.Records = (_, _) => FakeReader.Empty with { SandboxDates = [new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27)] };

        var account = (await RunAsync(reader, Ist(31, 12, 0))).Report.Accounts.Single();

        account.Counted.ShouldBe(0);
        account.SandboxSessions.ShouldBe(2);
        account.Label.ShouldContain("sandbox sessions are not counted: 2");
    }

    [Theory]
    [InlineData(16, 0, 29)]
    [InlineData(16, 39, 29)]
    [InlineData(16, 40, 30)]
    public async Task The_default_end_is_the_last_session_whose_final_run_is_due(int h, int m, int day)
    {
        var view = await RunAsync(new FakeReader(), Ist(30, h, m));

        view.Report.Through.ShouldBe(new DateOnly(2026, 10, day));
    }

    [Fact]
    public async Task An_explicit_date_on_a_weekend_takes_the_session_before_it_and_a_later_date_is_refused()
    {
        var view = await RunAsync(new FakeReader(), Ist(31, 12, 0), new DateOnly(2026, 10, 25));
        var later = await Handler(new FakeReader(), Ist(31, 12, 0)).HandleAsync(new EvaluateExecutionPhaseCheckQuery(new DateOnly(2026, 11, 20), null), Ct);

        view.Report.Through.ShouldBe(new DateOnly(2026, 10, 23));
        later.IsFailure.ShouldBeTrue();
        later.Error.Code.ShouldBe("execution.invalid");
    }

    [Fact]
    public async Task An_unknown_account_is_refused()
    {
        var result = await Handler(new FakeReader(), Ist(31, 12, 0)).HandleAsync(new EvaluateExecutionPhaseCheckQuery(null, Guid.NewGuid()), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("execution.invalid");
    }

    [Fact]
    public async Task The_check_only_reads_and_gives_the_same_bytes_twice()
    {
        var reader = SemiAutoReader();
        var allowed = new[] { nameof(FakeReader.AccountsAsync), nameof(FakeReader.TrailAsync), nameof(FakeReader.SettingsAsync), nameof(FakeReader.NewestSessionAsync), nameof(FakeReader.RecordsAsync) };

        var first = await RunAsync(reader, Ist(31, 12, 0));
        var second = await RunAsync(reader, Ist(31, 12, 0));

        first.Text.ShouldBe(second.Text);
        reader.Calls.ShouldAllBe(c => allowed.Contains(c));
        typeof(IExecutionRecordsReader).GetMethods().ShouldAllBe(m => m.Name.EndsWith("Async", StringComparison.Ordinal) && allowed.Contains(m.Name));
    }
}
