using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 part 12b, AC5 on real seeded data: 100 graded shadow trades of one strategy, net of a confirmed charge sheet, at -1.0 R (0.1 R of charges each), so cost stress fires (the net at 1.5 times cost is -1.05; the sequential failure boundary does not, because one session of 100 candidates has a large design effect). The daily run, resolved from
/// the container as the service resolves it: with the setting OFF (no row) it writes the proposal and a derived notice and changes nothing else; with the setting ON (the owner's switch, through the endpoint) it pauses the cells of the strategy as <c>auto</c> and the bell shows it; a second run of the day does nothing. The failure boundary
/// being left to the owner is tested in the unit tests and in <c>KillProposalsApiTests</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DecayMonitorRunApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Session = new(2099, 3, 4);

    private readonly string _strategy = "DM" + Guid.NewGuid().ToString("N")[..10];

    private string Cell(string side) => $"{_strategy}|15m|{side}|intraday|FIXED_RR|*";

    private async Task SetDatabaseClockAsync(DateTimeOffset? at)
    {
        var body = at is { } t ? $"SELECT '{t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}+00'::timestamptz" : "SELECT clock_timestamp()";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql VOLATILE AS $fn$ {body} $fn$");
    }

    public override async ValueTask DisposeAsync()
    {
        await SetDatabaseClockAsync(null);
        await base.DisposeAsync();
    }

    /// <summary>The fixture charge sheet stored and confirmed as the owner's, and 100 graded shadow trades net of it.</summary>
    private async Task SeedSeriesAsync(int count = 100)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Platform.IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_on', 'owner:test@example.com', 'test'); INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', '{FixtureSheet.Id}', '{FixtureSheet.Sheet.RatesSha256}', 'owner:test@example.com', 'test')");

        await SetDatabaseClockAsync(new DateTimeOffset(2099, 3, 4, 2, 0, 0, TimeSpan.Zero));
        var plan = await ScalarAsync<long>(
            "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, @d, @c, 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', @at) RETURNING id",
            ("k", "dm-" + Guid.NewGuid().ToString("N")), ("d", Session), ("c", new DateTimeOffset(2099, 3, 4, 3, 35, 0, TimeSpan.Zero)), ("at", new DateTimeOffset(2099, 3, 3, 15, 40, 0, TimeSpan.Zero)));
        await ExecuteAsync(connection, "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES (@d, @n, 'ok', repeat('a', 64), NULL, '2000-01-01')", ("d", Session), ("n", count));
        for (var i = 0; i < count; i++)
        {
            var item = await ScalarAsync<long>(
                "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, @r, 'primary', 's', 'BUY', @s, 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}') RETURNING id",
                ("p", plan), ("r", i + 1), ("s", _strategy));
            await ExecuteAsync(
                connection,
                "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, written_at, row_hash) VALUES (@d, @p, @i, @k, 'primary', 'c', repeat('a', 64), 's', 'BUY', 'v', true, '2000-01-01', repeat('b', 64))",
                ("d", Session), ("p", plan), ("i", item), ("k", $"{_strategy}|S{i}|Buy|2099-03-04"));
        }

        await SetDatabaseClockAsync(new DateTimeOffset(2099, 3, 4, 11, 0, 0, TimeSpan.Zero));
        await ExecuteAsync(
            connection,
            "INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, sheet_id, r_gross, engine_version, graded_at) SELECT id, 'graded', -1.0, 'net', @sheet, -0.9, 'sim-6', '2000-01-01' FROM paper.shadow_entries WHERE trade_key LIKE @prefix",
            ("sheet", FixtureSheet.Id), ("prefix", _strategy + "|%"));
    }

    private async Task WalkAsync(string cell, params CellState[] states)
    {
        using var scope = Factory.Services.CreateScope();
        var record = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>();
        foreach (var state in states)
        {
            (await record.HandleAsync(new RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:setup"]), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    private async Task<DecayRunOutcome> RunAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>>().HandleAsync(new EvaluateDecayCommand(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private Task<long> PausedAsync(string cell) => ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject = @s AND to_state = 'paused'", ("s", cell));

    [Fact]
    public async Task With_the_setting_off_the_daily_run_writes_the_proposals_and_a_notice_and_changes_nothing_else()
    {
        await SeedSeriesAsync();
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        await WalkAsync(Cell("SELL"), CellState.Candidate, CellState.Paper, CellState.Active);
        var rows = await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject LIKE @p", ("p", _strategy + "|%"));

        var first = await RunAsync();
        var second = await RunAsync();

        first.ShouldBe(new DecayRunOutcome(1, 1, 0, 1, 0, 0));
        second.ShouldBe(new DecayRunOutcome(1, 0, 1, 0, 0, 0), "the strategy was evaluated today");
        (await ScalarAsync<string>("SELECT string_agg(rule || ':' || state || ':' || proposal_only::text, ',' ORDER BY rule) FROM confidence.kill_proposals WHERE strategy = @s", ("s", _strategy))).ShouldBe("cost_stress:proposed:false");
        (await ScalarAsync<string>("SELECT cells::text FROM confidence.kill_proposals WHERE strategy = @s AND rule = 'cost_stress'", ("s", _strategy))).ShouldContain(Cell("BUY"));
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.decay_checks WHERE strategy = @s", ("s", _strategy))).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0, "no row means OFF");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject LIKE @p", ("p", _strategy + "|%"))).ShouldBe(rows, "no cell moved");
        var notices = (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "kill_proposed" && i.GetProperty("text").GetString()!.Contains(_strategy, StringComparison.Ordinal)).ToList();
        notices.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_the_setting_on_the_daily_run_pauses_the_cells_as_auto_for_cost_stress_and_shows_it_on_the_bell()
    {
        await SeedSeriesAsync();
        await WalkAsync(Cell("BUY"), CellState.Candidate, CellState.Paper);
        await WalkAsync(Cell("SELL"), CellState.Candidate, CellState.Paper, CellState.Active);
        using var switched = await Client.PutAsJsonAsync(new Uri("/api/confidence/kill-settings", UriKind.Relative), new { auto = true, reason = "I have read two proposals", password = Factory.OwnerPassword }, Ct);
        switched.StatusCode.ShouldBe(HttpStatusCode.OK);
        var switchId = JsonDocument.Parse(await switched.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("id").GetInt64();

        var outcome = await RunAsync();

        outcome.ShouldBe(new DecayRunOutcome(1, 1, 0, 1, 1, 0));
        (await PausedAsync(Cell("BUY"))).ShouldBe(1);
        (await PausedAsync(Cell("SELL"))).ShouldBe(1);
        (await ScalarAsync<string>("SELECT string_agg(DISTINCT actor, ',') FROM research.learning_changelog WHERE subject LIKE @p AND to_state = 'paused'", ("p", _strategy + "|%"))).ShouldBe("auto");
        (await ScalarAsync<string>("SELECT string_agg(DISTINCT before_ref, ',') FROM research.learning_changelog WHERE subject LIKE @p AND to_state = 'paused'", ("p", _strategy + "|%"))).ShouldBe($"kill-switch:{switchId}");
        (await ScalarAsync<long>("SELECT count(*) FROM research.learning_changelog WHERE subject LIKE @p AND actor LIKE 'owner:%' AND to_state = 'paused'", ("p", _strategy + "|%"))).ShouldBe(0, "never recorded as an owner approval");
        (await ScalarAsync<string>("SELECT string_agg(rule || ':' || state || ':' || COALESCE(actor, '-') || ':' || COALESCE(auto_setting_id::text, '-'), ',' ORDER BY rule) FROM confidence.kill_proposals WHERE strategy = @s", ("s", _strategy)))
            .ShouldBe($"cost_stress:applied:auto:{switchId}");

        var items = (await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/plans/notices", UriKind.Relative), Ct)).GetProperty("items").EnumerateArray().Where(i => i.GetProperty("text").GetString()!.Contains(_strategy, StringComparison.Ordinal)).ToList();
        items.Select(i => i.GetProperty("kind").GetString()).Order(StringComparer.Ordinal).ToList().ShouldBe(["kill_applied_auto"], "the automatic pause is visible on the bell as a resolved notice");
    }
}
