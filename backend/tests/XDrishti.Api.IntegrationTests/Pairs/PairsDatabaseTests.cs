using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Pairs;
using XDrishti.Domain.Lab;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Api.IntegrationTests.Pairs;

/// <summary>
/// P14-13 on a real TimescaleDB: both verbs' use cases over a seeded bar set. Three F&amp;O shares in one sector have 300 daily closes (one pair constructed cointegrated, one independent walk) and the trading day has a planted excursion of the Y leg to z 2.2 and back. The run counts the six
/// variants and the candidates in the ledger, stores the formation and the one short-spread trade, and a second run writes nothing new; the evaluation stores a protocol S record that the shared route lists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PairsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Clean = "TRUNCATE research.pair_trades, research.pair_formations; DELETE FROM lab.test_ledger WHERE strategy_id = 'pairs:intraday-1'; DELETE FROM research.extension_evaluations WHERE family = 'pairs:intraday-1'";

    private static readonly DateOnly Quarter = new(2026, 10, 1);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, Clean);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Clean);
        }

        await base.DisposeAsync();
    }

    private async Task<Guid> ShareAsync(string ticker, double[] lnClose)
    {
        var id = await SeedAsync(ticker);
        var days = Sessions(new DateOnly(2026, 9, 30), lnClose.Length);
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "UPDATE market.instruments SET fno_eligible = true, sector = 'Banks', tick_size = 5 WHERE id = @id", ("id", id));
        await ExecuteAsync(
            connection,
            """
            INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
            SELECT @id, '1D', d.ts, d.c, d.c, d.c, d.c, 1000, 375, true FROM unnest(@ts, @close) AS d(ts, c)
            """,
            ("id", id), ("ts", days.Select(d => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), India).ToUniversalTime()).ToArray()), ("close", lnClose.Select(Math.Exp).ToArray()));
        return id;
    }

    private static List<DateOnly> Sessions(DateOnly last, int count)
    {
        var days = new List<DateOnly>();
        for (var d = last; days.Count < count; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        days.Reverse();
        return days;
    }

    /// <summary>A session of flat minutes from 09:15 to 15:29, each 5-minute bucket at one price given by the SQL expression of the bucket number <c>b</c>.</summary>
    private async Task MinutesAsync(Guid id, DateOnly day, string priceOfBucket)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source)
            SELECT @id, q.ts, q.p, q.p, q.p, q.p, 100, 'dhan'
            FROM (SELECT g.ts, {priceOfBucket} AS p FROM (SELECT ts, floor(extract(epoch FROM (ts - @start)) / 300)::int AS b FROM generate_series(@start, @start + interval '374 minutes', interval '1 minute') ts) g) q
            """,
            ("id", id), ("start", new DateTimeOffset(day.Year, day.Month, day.Day, 9, 15, 0, India).ToUniversalTime()));
    }

    private async Task<(Guid Y, Guid X)> SeedAsync()
    {
        var rng = new SeededRandom(7);
        double Normal() => Math.Sqrt(-2.0 * Math.Log(((rng.NextBits() >> 11) + 0.5) / 9007199254740992.0)) * Math.Cos(2.0 * Math.PI * ((rng.NextBits() >> 11) + 0.5) / 9007199254740992.0);
        double[] Walk(double start)
        {
            var w = new double[300];
            w[0] = start;
            for (var i = 1; i < 300; i++)
            {
                w[i] = w[i - 1] + (0.01 * Normal());
            }

            return w;
        }

        var x = Walk(4.6);
        var u = new double[300];
        for (var i = 1; i < 300; i++)
        {
            u[i] = (0.85 * u[i - 1]) + (0.003 * Normal());
        }

        var y = x.Select((v, i) => 1.0 + (0.8 * v) + u[i]).ToArray();
        var yId = await ShareAsync("PAIRY", y);
        var xId = await ShareAsync("PAIRX", x);
        await ShareAsync("PAIRC", Walk(4.5));
        foreach (var day in new[] { new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 30) })
        {
            await MinutesAsync(yId, day, "100.0 * exp(CASE WHEN g.b % 2 = 0 THEN 0.004 ELSE -0.004 END)");
            await MinutesAsync(xId, day, "100.0");
        }

        await MinutesAsync(yId, Quarter, "CASE WHEN g.b < 20 THEN 100.0 WHEN g.b < 22 THEN 100.90 ELSE 100.15 END");
        await MinutesAsync(xId, Quarter, "100.0");
        return (yId, xId);
    }

    private async Task<PairsRunSummary> RunAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunPairsCommand, PairsRunSummary>>().HandleAsync(new RunPairsCommand(Quarter, Quarter, [PairVariant.Primary]), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public async Task The_run_counts_the_variant_and_the_candidate_in_the_ledger_stores_the_formation_and_the_planted_trade_and_a_second_run_writes_nothing_new()
    {
        await SeedAsync();

        var first = await RunAsync();

        first.FormationsWritten.ShouldBe(1);
        first.TradesWritten.ShouldBe(1);
        first.Trials.ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(DISTINCT strategy_hash) FROM lab.test_ledger WHERE strategy_id = 'pairs:intraday-1'")).ShouldBe(2);
        (await ScalarAsync<long>("SELECT count(*) FROM research.pair_formations WHERE family = 'pairs:intraday-1' AND quarter = '2026Q4' AND selected = 1 AND candidates = 1")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT side || '|' || exit_reason || '|' || (r > 0)::text || '|' || array_to_string(labels, ',') FROM research.pair_trades")).ShouldStartWith("short_spread|exit|true|variant:e2.0/L5,engine:pair-1,fills:sim-9,cfg:");
        (await ScalarAsync<string>("SELECT to_char(entry_ts AT TIME ZONE 'Asia/Kolkata', 'HH24:MI') || ' ' || to_char(exit_ts AT TIME ZONE 'Asia/Kolkata', 'HH24:MI') FROM research.pair_trades")).ShouldBe("11:00 11:10");
        (await ScalarAsync<string>("SELECT array_to_string(labels, ',') FROM research.pair_trades")).ShouldContain("gross");
        var ledgerRows = await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = 'pairs:intraday-1'");

        var second = await RunAsync();

        second.FormationsWritten.ShouldBe(0);
        second.TradesWritten.ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM lab.test_ledger WHERE strategy_id = 'pairs:intraday-1'")).ShouldBe(ledgerRows);
        (await ScalarAsync<long>("SELECT count(*) FROM research.pair_trades")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM research.pair_formations")).ShouldBe(1);
    }

    [Fact]
    public async Task The_evaluation_stores_a_protocol_S_not_evaluable_record_that_the_shared_route_lists_and_a_re_run_stores_nothing()
    {
        await SeedAsync();
        await RunAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluatePairsCommand, ExtensionEvaluationOutcome>>();
            var first = await handler.HandleAsync(new EvaluatePairsCommand(EvaluatePairsCommand.IntradayFamily, false), Ct);
            var second = await handler.HandleAsync(new EvaluatePairsCommand(EvaluatePairsCommand.IntradayFamily, false), Ct);

            first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
            first.Value.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
            first.Value.Records.Single().Stored.ShouldBeTrue();
            second.Value.Records.Single().Stored.ShouldBeFalse();
            first.Value.Note.ShouldNotBeNull().ShouldContain("ledger: N 2, F ");
        }

        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations WHERE family = 'pairs:intraday-1' AND protocol = 'S' AND fold_unit = 'quarter'")).ShouldBe(1);
        using var response = await Client.GetAsync(new Uri("/api/research/extensions?family=pairs:intraday-1", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var record = list.GetProperty("items")[0];
        record.GetProperty("protocol").GetString().ShouldBe("S");
        record.GetProperty("verdict").GetString().ShouldBe("NOT_EVALUABLE");
        record.GetProperty("missing").EnumerateArray().Select(m => m.GetString()).ShouldContain(m => m!.StartsWith("quarters ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_evaluation_before_any_run_is_refused_because_the_primary_variant_is_not_in_the_ledger()
    {
        using var scope = Factory.Services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluatePairsCommand, ExtensionEvaluationOutcome>>().HandleAsync(new EvaluatePairsCommand(EvaluatePairsCommand.IntradayFamily, false), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.trial_not_in_ledger");
        (await ScalarAsync<long>("SELECT count(*) FROM research.extension_evaluations WHERE family = 'pairs:intraday-1'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_shared_route_still_refuses_an_unknown_family_and_names_the_pairs_family_in_the_message()
    {
        using var response = await Client.GetAsync(new Uri("/api/research/extensions?family=pairs:intraday-9", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("extensions.invalid_query");
        text.ShouldContain("pairs:intraday-1");
    }
}
