using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using XDrishti.Api.Simulate.Assist;
using XDrishti.Api.Simulate.Gate;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-09e: a VWAP_PULLBACK row and a BASE_BREAKOUT row driven through the assisted read and the gate with real sessions (the history read, the second-source splice, the cited minutes, the parameters read from strings,
/// the decision date taken from the trade key, the history caveat). The hand fixtures are those of the reader tests: V1 (an up-trend prefix of 21 sessions and the six-bar day) and a rising series of 80 sessions with a breakout on the last.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class MultiSessionAssistTests
{
    private static readonly DateOnly VwapDate = new(2026, 3, 2);

    private static long Midnight(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();

    /// <summary>A session whose minute <c>k</c> (0 = 09:15) is the given bar when listed, else repeats the previous close; volume 1000 a minute times <paramref name="volumeFactor"/>.</summary>
    private static ReaderSession Session(DateOnly date, decimal volumeFactor, params (int Minute, decimal O, decimal H, decimal L, decimal C)[] set)
    {
        var bars = new List<ReaderBar>();
        var previous = set[0].C;
        for (var k = 0; k < 375; k++)
        {
            var bar = set.FirstOrDefault(s => s.Minute == k);
            var (o, h, l, c) = bar == default ? (previous, previous, previous, previous) : (bar.O, bar.H, bar.L, bar.C);
            bars.Add(new ReaderBar(Midnight(date) + (((9 * 60) + 15 + k) * 60L), o, h, l, c, 1000m * volumeFactor));
            previous = c;
        }

        return new ReaderSession(date, bars);
    }

    private static List<ReaderSession> VwapPrefix()
    {
        var sessions = new List<ReaderSession>();
        for (var k = 1; k <= 21; k++)
        {
            var c = 100m + (0.5m * k);
            sessions.Add(Session(VwapDate.AddDays(k - 22), 1m, [.. Enumerable.Range(0, 25).Select(b => (b * 15, c, c + 0.5m, c - 0.5m, c))]));
        }

        return sessions;
    }

    /// <summary>V1: six bars at 09:15 to 10:30, the target bar at 11:30. Entry 113.75 at 10:45, stop 112.50, target 116.25, exit 11:30.</summary>
    private static ReaderSession VwapDay() => Session(VwapDate, 1m, (0, 110.5m, 111.5m, 110.5m, 111.25m), (15, 111.25m, 112.25m, 111.25m, 112m), (30, 112m, 113m, 112m, 112.75m), (45, 112.75m, 113.75m, 112.75m, 113.5m), (60, 113.5m, 113.5m, 112.5m, 113m), (75, 113m, 114m, 113m, 113.75m), (135, 113.75m, 116.25m, 113.75m, 116m));

    private static List<DateOnly> Weekdays(int count)
    {
        var dates = new List<DateOnly>();
        for (var d = new DateOnly(2026, 1, 5); dates.Count < count; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(d);
            }
        }

        return dates;
    }

    /// <summary>80 sessions rising 100.0 to 107.9 (the last has twice the volume), then 6 flat at 108.00: signal on the 80th, entry 108.00 at the open of the 81st, stop 105.50, target 113.00, max hold at the close of the 85th.</summary>
    private static List<ReaderSession> BreakoutSeries(decimal signalVolume = 2m)
    {
        var dates = Weekdays(86);
        var sessions = new List<ReaderSession>();
        for (var i = 0; i < 80; i++)
        {
            var c = 100m + (0.1m * i);
            sessions.Add(Session(dates[i], i == 79 ? signalVolume : 1m, (0, c, c, c - 0.5m, c)));
        }

        for (var i = 80; i < 86; i++)
        {
            sessions.Add(Session(dates[i], 1m, (0, 108m, 108m, 108m, 108m)));
        }

        return sessions;
    }

    private sealed class Stored(IReadOnlyList<ReaderSession> sessions, string? caveat = null) : IStoredDays
    {
        public ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(new StoredDay(0.05m, sessions.FirstOrDefault(s => s.Date == date)?.Bars ?? []));

        public ValueTask<StoredRange> GetRangeAsync(string symbol, DateOnly from, DateOnly to, DateOnly lastUsed, DateOnly? admittedSeam, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new StoredRange(0.05m, [.. sessions.Where(s => s.Date >= from && s.Date <= to)], caveat));
    }

    private sealed class DatedSecond(IReadOnlyDictionary<DateOnly, IReadOnlyList<ReaderBar>> days) : ISecondSource
    {
        public List<DateOnly> Calls { get; } = [];

        public string Name => "fake";

        public ValueTask<SecondDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
        {
            Calls.Add(date);
            return ValueTask.FromResult(days.TryGetValue(date, out var bars) ? new SecondDay(bars, null) : SecondDay.Missing("no such day"));
        }
    }

    private static DatedSecond SecondOf(IEnumerable<ReaderSession> sessions) => new(sessions.ToDictionary(s => s.Date, s => s.Bars));

    private static MemoryFolder PackOf(string strategy, string parameters, string tradeKey, DateOnly entryDate, ReaderReading engineSide, Action<Dictionary<string, string>>? change = null)
    {
        var folder = new MemoryFolder();
        folder.Files[PackFiles.PackJsonName] = Encoding.UTF8.GetBytes(
            "{\"format\":\"" + PackFiles.Format + "\",\"pack_id\":\"t-1\",\"pack_key\":\"" + PackKey + "\",\"count\":1,\"seed\":\"seed-1\",\"runs\":[{\"strategy\":\"" + strategy + "\",\"parameters\":" + parameters + ",\"run_key\":\"rk\"}],\"rows\":[{\"seq\":1,\"trade_key\":\""
            + tradeKey + "\",\"symbol\":\"" + Symbol + "\",\"side\":\"Buy\",\"date\":\"" + entryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\",\"run_key\":\"rk\"}],\"spots\":[]}");
        folder.Files[PackFiles.EngineValuesName] = EngineValuesFor(tradeKey, engineSide, change);
        folder.Files[PackFiles.CheckLogName] = Encoding.UTF8.GetBytes("seq,trade_key\n1,x\n");
        return Seal(folder);
    }

    private const string VwapParameters = "{\"pull_atr\":\"0.5\",\"rr\":\"2\"}";

    private const string BreakoutParameters = "{\"base_len\":\"20\",\"base_pct\":\"8\",\"rr\":\"2\",\"vol_x\":\"1.5\"}";

    private static ReaderReading VwapReading(List<ReaderSession> prior, ReaderSession day) => VwapPullbackReader.Read(prior, day.Bars, VwapPullbackStrategy.V1(0.05m), ReaderSide.Buy)!;

    private static ReaderReading BreakoutReading(List<ReaderSession> all) => BaseBreakoutReader.Read(all, all[79].Date, BaseBreakoutStrategy.V1(0.05m))!;

    private static string Key(string strategy, DateOnly date) => strategy + "|" + Symbol + "|Buy|" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_VWAP_PULLBACK_row_is_read_on_the_stored_history_and_agrees_with_an_engine_that_follows_the_rules()
    {
        var prior = VwapPrefix();
        var day = VwapDay();
        var folder = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day));

        var outcome = await AssistRunner.RunAsync(folder, SecondOf([day]), new Stored([.. prior, day]), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(0, string.Join('\n', outcome.Lines));
        var verdict = outcome.Details!.Verdicts.Single();
        verdict.Verdict.ShouldBe(Verdicts.AgreeAll);
        verdict.Row.StoredReading.ShouldNotBeNull().ExitReason.ShouldBe(ReaderExitReason.Target);
        verdict.Row.Bars.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_VWAP_PULLBACK_second_source_day_is_read_on_the_stored_history_and_a_changed_feed_is_data_sensitive()
    {
        var prior = VwapPrefix();
        var day = VwapDay();
        var folder = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day));
        var flat = Session(VwapDate, 1m, (0, 110.5m, 110.5m, 110.5m, 110.5m)); // the second source shows no pattern

        var outcome = await AssistRunner.RunAsync(folder, SecondOf([flat]), new Stored([.. prior, day]), TestContext.Current.CancellationToken);

        outcome.Details!.Verdicts.Single().Verdict.ShouldBe(Verdicts.DataSensitive);
    }

    [Fact]
    public async Task A_BASE_BREAKOUT_row_dated_on_its_entry_day_is_read_from_the_decision_date_of_its_key_and_the_second_source_is_asked_for_both_days()
    {
        var all = BreakoutSeries();
        var signal = all[79].Date;
        var entry = all[80].Date;
        var folder = PackOf("BASE_BREAKOUT", BreakoutParameters, Key("BASE_BREAKOUT", signal), entry, BreakoutReading(all));
        var second = SecondOf([all[79], all[80]]);

        var outcome = await AssistRunner.RunAsync(folder, second, new Stored(all), TestContext.Current.CancellationToken);

        entry.ShouldNotBe(signal);
        outcome.Exit.ShouldBe(0, string.Join('\n', outcome.Lines));
        second.Calls.OrderBy(d => d).ShouldBe([signal, entry]);
        var verdict = outcome.Details!.Verdicts.Single();
        verdict.Verdict.ShouldBe(Verdicts.AgreeAll);
        verdict.Row.StoredReading.ShouldNotBeNull().ExitReason.ShouldBe(ReaderExitReason.MaxHold);
        verdict.Row.Bars.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_BASE_BREAKOUT_signal_day_volume_from_the_second_source_replaces_the_stored_one()
    {
        var all = BreakoutSeries();
        var folder = PackOf("BASE_BREAKOUT", BreakoutParameters, Key("BASE_BREAKOUT", all[79].Date), all[80].Date, BreakoutReading(all));
        var thin = BreakoutSeries(signalVolume: 1.4m); // the second source shows too little volume on the signal day

        var outcome = await AssistRunner.RunAsync(folder, SecondOf([thin[79], all[80]]), new Stored(all), TestContext.Current.CancellationToken);

        var verdict = outcome.Details!.Verdicts.Single();
        verdict.Verdict.ShouldBe(Verdicts.DataSensitive);
        verdict.Fields.ShouldBeEmpty();
        verdict.SecondFields.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_BASE_BREAKOUT_row_is_unavailable_when_either_day_is_missing_from_the_second_source()
    {
        var all = BreakoutSeries();
        var folder = PackOf("BASE_BREAKOUT", BreakoutParameters, Key("BASE_BREAKOUT", all[79].Date), all[80].Date, BreakoutReading(all));

        var outcome = await AssistRunner.RunAsync(folder, SecondOf([all[79]]), new Stored(all), TestContext.Current.CancellationToken);

        outcome.Details!.Verdicts.Single().Verdict.ShouldBe(Verdicts.SourceUnavailable);
    }

    [Theory]
    [InlineData("VWAP_PULLBACK", "{\"pullatr\":\"0.5\",\"rr\":\"2\"}", "pull_atr")]
    [InlineData("VWAP_PULLBACK", "{\"pull_atr\":\"0.5\"}", "rr")]
    [InlineData("BASE_BREAKOUT", "{\"base_len\":\"20\",\"base_pct\":\"8\",\"rr\":\"2\"}", "vol_x")]
    public async Task A_missing_or_misspelled_parameter_refuses_the_pack_with_exit_2_and_nothing_is_read(string strategy, string parameters, string lacking)
    {
        var all = BreakoutSeries();
        var folder = PackOf(strategy, parameters, Key(strategy, strategy == "BASE_BREAKOUT" ? all[79].Date : all[80].Date), all[80].Date, BreakoutReading(all));
        var second = SecondOf([all[79], all[80]]);

        var outcome = await AssistRunner.RunAsync(folder, second, new Stored(all), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(2);
        outcome.Lines.ShouldContain(l => l.Contains("lacks the parameter " + lacking, StringComparison.Ordinal));
        second.Calls.ShouldBeEmpty();
        folder.Ops.ShouldNotContain(o => o.StartsWith("write ", StringComparison.Ordinal));
    }

    private const string Caveat = "a corporate action (ex-date 2026-02-20) lies inside the history";

    [Fact]
    public async Task A_caveated_row_that_agrees_is_a_data_finding()
    {
        var prior = VwapPrefix();
        var day = VwapDay();
        var folder = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day));

        var outcome = await GateRunner.RunAsync(folder, SecondOf([day]), new Stored([.. prior, day], Caveat), TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(GateStatus.PassWithDataFindings);
        outcome.Exit.ShouldBe(0);
        Encoding.UTF8.GetString(folder.Files[GateRunner.JsonName]).ShouldContain("history not machine-checkable");
    }

    [Fact]
    public async Task A_caveated_row_that_differs_needs_a_hand_check_exit_10_and_without_the_caveat_it_is_a_defect()
    {
        var prior = VwapPrefix();
        var day = VwapDay();
        Action<Dictionary<string, string>> wrongExit = c => c["exit_price"] = "100";
        var plain = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day), wrongExit);
        var caveated = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day), wrongExit);

        var blocked = await GateRunner.RunAsync(plain, SecondOf([day]), new Stored([.. prior, day]), TestContext.Current.CancellationToken);
        var hand = await GateRunner.RunAsync(caveated, SecondOf([day]), new Stored([.. prior, day], Caveat), TestContext.Current.CancellationToken);

        blocked.Status.ShouldBe(GateStatus.BlockedDefect);
        hand.Status.ShouldBe(GateStatus.RerunNeeded);
        hand.Exit.ShouldBe(10);
        var markdown = Encoding.UTF8.GetString(caveated.Files[GateRunner.MarkdownName]);
        markdown.ShouldContain("not machine-checkable: hand check needed");
        markdown.ShouldContain("Hand check needed:");
        markdown.ShouldNotContain("Reconnect the second source");
    }

    [Fact]
    public async Task A_caveated_row_where_the_reader_finds_no_trade_needs_a_hand_check_too()
    {
        var prior = VwapPrefix();
        var day = VwapDay();
        var folder = PackOf("VWAP_PULLBACK", VwapParameters, Key("VWAP_PULLBACK", VwapDate), VwapDate, VwapReading(prior, day));
        var flat = Session(VwapDate, 1m, (0, 110.5m, 110.5m, 110.5m, 110.5m)); // the stored day shows no pattern: the reader reads no trade

        var outcome = await GateRunner.RunAsync(folder, SecondOf([flat]), new Stored([.. prior, flat], Caveat), TestContext.Current.CancellationToken);

        outcome.Status.ShouldBe(GateStatus.RerunNeeded);
        outcome.Exit.ShouldBe(10);
    }

    [Theory]
    [InlineData("VWAP_PULLBACK|NSE:AAA|Buy|not-a-date", "2026-03-02", "does not end in a yyyy-MM-dd decision date")]
    [InlineData("VWAP_PULLBACK|NSE:AAA|Buy|2026-02-27", "2026-03-02", "intraday trade whose decision date 2026-02-27 is not its date 2026-03-02")]
    [InlineData("BASE_BREAKOUT|NSE:AAA|Buy|2026-03-02", "2026-03-02", "swing trade whose decision date 2026-03-02 is not before its entry date 2026-03-02")]
    public async Task A_trade_key_whose_date_does_not_fit_the_strategy_refuses_the_pack_naming_the_row_and_the_key(string key, string date, string text)
    {
        var strategy = key[..key.IndexOf('|', StringComparison.Ordinal)];
        var prior = VwapPrefix();
        var folder = PackOf(strategy, strategy == "VWAP_PULLBACK" ? VwapParameters : BreakoutParameters, key, DateOnly.Parse(date, CultureInfo.InvariantCulture), VwapReading(prior, VwapDay()));
        var second = SecondOf([VwapDay()]);

        var outcome = await AssistRunner.RunAsync(folder, second, new Stored([.. prior, VwapDay()]), TestContext.Current.CancellationToken);

        outcome.Exit.ShouldBe(2);
        outcome.Lines.ShouldContain(l => l.Contains("pack row 1 (trade key '" + key + "')", StringComparison.Ordinal) && l.Contains(text, StringComparison.Ordinal));
        second.Calls.ShouldBeEmpty();
        folder.Ops.ShouldNotContain(o => o.StartsWith("write ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_source_day_with_no_bars_is_source_unavailable_and_never_an_empty_session_in_the_splice()
    {
        var all = BreakoutSeries();
        var folder = PackOf("BASE_BREAKOUT", BreakoutParameters, Key("BASE_BREAKOUT", all[79].Date), all[80].Date, BreakoutReading(all));
        var empty = new Dictionary<DateOnly, IReadOnlyList<ReaderBar>> { [all[79].Date] = all[79].Bars, [all[80].Date] = [] };

        var outcome = await AssistRunner.RunAsync(folder, new DatedSecond(empty), new Stored(all), TestContext.Current.CancellationToken);

        var verdict = outcome.Details!.Verdicts.Single();
        verdict.Verdict.ShouldBe(Verdicts.SourceUnavailable);
        verdict.Row.Unavailable.ShouldNotBeNull().ShouldContain("no bars");
    }
}
