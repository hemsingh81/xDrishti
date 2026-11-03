using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Learning;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>
/// P14-09 on a real TimescaleDB: the select-only reads of one expression run. The windows are in the query (side, India entry dates with UTC bounds), the unpaired counts leave out the paired rows and the ones open at the end, the dates carry no figure, the latest run of a cash run and a profile
/// is the one found, and the use case is wired and refuses an unknown run before the ledger.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionReaderDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Truncate = "TRUNCATE research.expression_trades, research.expression_runs; DELETE FROM lab.test_ledger WHERE strategy_id = 'expr:option_buy'; DELETE FROM research.extension_evaluations WHERE family = 'expr:option_buy'; DELETE FROM research.sim_runs WHERE run_key = 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'";

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    private static readonly string Cash = new('a', 64);
    private static readonly string Profile = new('b', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, Truncate);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        await base.DisposeAsync();
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute) => new(new DateOnly(2025, month, day).ToDateTime(new TimeOnly(hour, minute)), India);

    private static RemarkRow Paired(string key, string side, DateTimeOffset entry, double option, double cash, bool net, params string[] labels) =>
        new(key, RemarkFates.Paired, side, 'W', 1, new DateOnly(2025, 7, 1), "derived", 2_200_000, entry, entry.AddMinutes(30), 6000, 7000, 10.0, 1000m, net ? 900m : null, option, cash, "stop", 75, "sizable", labels);

    private static RemarkRow Unpaired(string key, string side, DateTimeOffset entry, string fate) =>
        new(key, fate, side, null, null, null, "none", null, entry, entry.AddMinutes(30), null, null, null, null, null, null, null, "stop", null, "n/a", []);

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task<string> SeedAsync(string runId, string cash, string profile, List<RemarkRow> rows) =>
        await WithAsync(async p =>
        {
            var written = await p.GetRequiredService<IExpressionTradeStore>().AppendAsync(new ExpressionRunRow(runId, cash, profile, "expr-1", "{}", rows.Count, rows.Count(r => r.IsPaired), "{}", rows.Any(r => r.IsPaired) ? 0.0 : null, []), rows, Ct);
            written.ShouldBeTrue();
            return runId;
        });

    private Task<T> ReadAsync<T>(Func<IExpressionReader, Task<T>> work) => WithAsync(p => work(p.GetRequiredService<IExpressionReader>()));

    private async Task<string> SeedMixedAsync()
    {
        List<RemarkRow> rows =
        [
            Paired("p1", "CE", At(6, 2, 10, 0), 1.5, 1.0, true, "bs_fallback"),
            Paired("p2", "CE", At(6, 30, 23, 59), 0.5, 1.0, true),      // the last minute of the window's last day (India)
            Paired("p3", "CE", At(7, 1, 0, 1), 2.0, 1.0, false),        // the first minute after it
            Paired("p4", "PE", At(6, 3, 10, 0), -1.0, 0.5, true),       // another side
            Unpaired("u1", "CE", At(6, 4, 10, 0), "spread_unavailable"),
            Unpaired("u2", "CE", At(6, 5, 10, 0), "spread_unavailable"),
            Unpaired("u3", "CE", At(6, 6, 10, 0), "no_series"),
            Unpaired("u4", "CE", At(6, 7, 10, 0), "open_at_end"),
            Unpaired("u5", "CE", At(7, 3, 10, 0), "no_series"),         // outside the window
        ];
        return await SeedAsync(new string('1', 64), Cash, Profile, rows);
    }

    [Fact]
    public async Task The_paired_trades_of_a_window_are_filtered_in_the_query_by_side_and_the_india_entry_date()
    {
        var id = await SeedMixedAsync();
        ExpressionWindow[] windows = [new("CE", new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30))];

        var rows = await ReadAsync(async r => await r.PairedAsync(id, windows, Ct));

        rows.Select(r => r.TradeKey).ShouldBe(["p1", "p2"]);
        rows[0].Labels.ShouldContain("bs_fallback");
        (rows[0].Net, rows[0].ExpirySource, rows[0].Sizable, rows[0].OptionR, rows[0].CashR).ShouldBe((true, "derived", "sizable", 1.5, 1.0));
        rows[0].EntryTs.Offset.ShouldBe(TimeSpan.Zero);
        var both = await ReadAsync(async r => await r.PairedAsync(id, [windows[0], new ExpressionWindow("PE", new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30))], Ct));
        both.Select(r => r.TradeKey).ShouldBe(["p1", "p4", "p2"]); // oldest entry first
        (await ReadAsync(async r => await r.PairedAsync(id, [], Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_unpaired_counts_leave_out_paired_rows_and_rows_open_at_the_end_and_obey_the_window()
    {
        var id = await SeedMixedAsync();

        var counts = await ReadAsync(async r => await r.UnpairedAsync(id, [new ExpressionWindow("CE", new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30))], Ct));
        var wide = await ReadAsync(async r => await r.UnpairedAsync(id, [new ExpressionWindow("CE", new DateOnly(2025, 6, 1), new DateOnly(2025, 7, 31))], Ct));

        counts.ShouldBe(new SortedDictionary<string, int> { ["no_series"] = 1, ["spread_unavailable"] = 2 });
        wide["no_series"].ShouldBe(2);
        wide.ShouldNotContainKey("open_at_end");
        wide.ShouldNotContainKey("paired");
    }

    [Fact]
    public async Task The_dates_carry_the_bounds_of_the_closed_trades_per_side_and_the_last_paired_day_and_no_figure()
    {
        var id = await SeedMixedAsync();

        var dates = await ReadAsync(async r => await r.DatesAsync(id, Ct));

        dates.Sides.ShouldBe([new SideBounds("CE", new DateOnly(2025, 6, 2), new DateOnly(2025, 7, 3)), new SideBounds("PE", new DateOnly(2025, 6, 3), new DateOnly(2025, 6, 3))]);
        dates.LastPaired.ShouldBe(new DateOnly(2025, 7, 1));
        var none = await ReadAsync(async r => await r.DatesAsync(new string('0', 64), Ct));
        none.Sides.ShouldBeEmpty();
        none.LastPaired.ShouldBeNull();
    }

    [Fact]
    public async Task The_latest_run_of_a_cash_run_and_a_profile_is_found_and_an_unknown_pair_is_null()
    {
        var first = await SeedAsync(new string('1', 64), Cash, Profile, [Unpaired("u", "CE", At(6, 4, 10, 0), "no_series")]);
        await Task.Delay(5, Ct);
        var second = await SeedAsync(new string('2', 64), Cash, Profile, [Unpaired("u", "CE", At(6, 4, 10, 0), "no_series")]);

        var found = await ReadAsync(async r => await r.FindRunAsync(Cash, Profile, Ct));

        first.ShouldNotBe(second);
        found.ShouldNotBeNull().RunId.ShouldBe(second);
        (await ReadAsync(async r => await r.FindRunAsync(Cash, new string('c', 64), Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task More_windows_than_one_read_takes_are_refused()
    {
        var windows = Enumerable.Range(0, IExpressionReader.MaxWindows + 1).Select(i => new ExpressionWindow("CE", new DateOnly(2025, 1, 1).AddDays(i), new DateOnly(2025, 1, 1).AddDays(i))).ToList();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await ReadAsync(async r => await r.PairedAsync(new string('1', 64), windows, Ct)));
    }

    [Fact]
    public async Task The_use_case_is_wired_and_refuses_an_unknown_run_before_anything_reaches_the_ledger()
    {
        var result = await WithAsync(async p => await p.GetRequiredService<ICommandHandler<EvaluateExpressionCommand, ExtensionEvaluationOutcome>>().HandleAsync(new EvaluateExpressionCommand("expr:option_buy", new string('9', 64), null, false), Ct));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBeOneOf("expression.profile_not_found", "expression.run_not_found");
        (await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = 'expr:option_buy'")).ShouldBe(0);
    }

    [Fact]
    public async Task A_full_run_re_marked_and_evaluated_stores_one_protocol_O_record_counts_one_trial_and_the_real_unique_index_refuses_a_second_hold_out_reading()
    {
        const string cashKey = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                """
                SET session_replication_role = replica;
                INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES ('ORB', 'intraday', true, now()) ON CONFLICT DO NOTHING;
                INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label, instruments, instruments_run,
                    trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, record_json, record_sha256, trades_sha256, created_at, created_by)
                VALUES ('cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc', 'sim-9', 5, 5, 'ORB', repeat('d', 64), '2024-01-02', '2026-12-31', 'basket', 'B1', 1, 1, 0, 0, 0, 0, false, 'modelled', '{}', repeat('e', 64), repeat('f', 64), '2026-12-31T05:00:00Z', 'lab:full');
                """);
        }

        var profile = XDrishti.Domain.Options.Remark.OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value;
        await WithAsync(async p => await p.GetRequiredService<IExpressionProfileStore>().AppendAsync(profile, Ct));
        var rng = new SeededRandom(42);
        static double Normal(SeededRandom r) => Math.Sqrt(-2.0 * Math.Log((((r.NextBits() >> 11) + 0.5) * (1.0 / 9007199254740992.0)))) * Math.Cos(2.0 * Math.PI * (((r.NextBits() >> 11) + 0.5) * (1.0 / 9007199254740992.0)));
        var rows = new List<RemarkRow>();
        for (var q = 0; q < 12; q++)
        {
            var qs = new DateOnly(2024, 1, 1).AddMonths(3 * q);
            for (var i = 0; i < 90; i++)
            {
                var cash = 0.05 + (0.8 * Normal(rng));
                var entry = new DateTimeOffset(qs.AddDays(i % 80).ToDateTime(new TimeOnly(10, 0)), India).AddSeconds(i);
                rows.Add(Paired("T" + q + "-" + i, "CE", entry, cash + 0.4 + (0.5 * Normal(rng)), cash, true));
            }
        }

        await SeedAsync(new string('9', 64), cashKey, profile.Hash, rows);

        async Task<XDrishti.Domain.Common.Result<ExtensionEvaluationOutcome>> Run(bool holdOut) =>
            await WithAsync(async p => await p.GetRequiredService<ICommandHandler<EvaluateExpressionCommand, ExtensionEvaluationOutcome>>().HandleAsync(new EvaluateExpressionCommand("expr:option_buy", cashKey, null, holdOut), Ct));

        var first = await Run(false);
        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        first.Value.Records.ShouldHaveSingleItem().Protocol.ShouldBe("O");
        first.Value.Records[0].Verdict.ShouldBe("KEEP", string.Join("; ", first.Value.Records[0].Missing));
        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations WHERE family = 'expr:option_buy' AND protocol = 'O'")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(DISTINCT strategy_hash) FROM lab.test_ledger WHERE strategy_id = 'expr:option_buy'")).ShouldBe(1);

        var again = await Run(false);
        again.Value.Records.Single().Stored.ShouldBeFalse();

        var hold = await Run(true);
        hold.IsSuccess.ShouldBeTrue();
        hold.Value.Records.Count.ShouldBe(2);
        hold.Value.Records[1].HoldOutRead.ShouldBeTrue();

        var second = await Run(true);
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("extensions.holdout_used");
        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations WHERE family = 'expr:option_buy' AND holdout_read")).ShouldBe(1);

        // the unique index itself refuses a second hold-out record that gets past the early check
        using var scope = Factory.Services.CreateScope();
        var stored = (await scope.ServiceProvider.GetRequiredService<IExtensionEvaluationStore>().GetAsync(hold.Value.Records[1].EvaluationId, Ct))!;
        var other = stored with { EvaluationId = new string('7', 64) };
        var refused = await scope.ServiceProvider.GetRequiredService<IExtensionEvaluationStore>().AppendAsync(other, DateTimeOffset.UtcNow, Ct);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("extensions.holdout_used");
    }
}
