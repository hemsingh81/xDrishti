using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Replay;

namespace XDrishti.Api.IntegrationTests.Replay;

/// <summary>
/// SR-04 part A over a real TimescaleDB: the owner-only simulation routes, the refusals with their status and code (422 sim.invalid, 409 sim.busy, sim.not_settled, sim.not_complete, sim.final), settle, advance, the day read, auto and cancel, and the database rules of the two tables (one active run, a
/// run keeps what it was started with, a snapshot is immutable). The runner is off in the tests, so a settled day is stored through the store port, as the runner would leave it.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimDaysApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly D1 = new(2026, 10, 5);
    private static readonly DateOnly D2 = new(2026, 10, 6);
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.sim_days_snapshots; DELETE FROM research.sim_days_runs;");
    }

    public override async ValueTask DisposeAsync()
    {
        Factory.ReplayStart.Gate = null;
        await using (var connection = await OpenAsync())
        {
            // leave nothing behind for the next class: a fixed hold-out block feeds the bell notices (holdout_stalled), a run or day feeds the simulation reads
            await ExecuteAsync(connection, "DELETE FROM research.sim_days_snapshots; DELETE FROM research.sim_days_runs;" + (_blockFixed ? " DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_blocks;" : string.Empty));
        }

        await base.DisposeAsync();
    }

    /// <summary>Fixes a hold-out block far after the test days, so the replay's clamp lets them be read.</summary>
    private bool _blockFixed;

    private async Task BlockAfterTheTestDaysAsync()
    {
        _blockFixed = true;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM lab.holdout_unlocks; DELETE FROM lab.holdout_releases; DELETE FROM lab.holdout_blocks; INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2031-01-01', '2026-03-02', '2031-06-30', 12, repeat('a', 64), now())");
    }

    private static NewSimDaysRun New()
    {
        var p = new ReplayParams(D1, D2, new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Learning);
        return new NewSimDaysRun(Guid.NewGuid(), new string('a', 64), ReplayJson.Canonical(p), """{"fill":{}}""", "{}", null, 1_000_000m, "learning", D1, D2, D2, D1, null, null, T0);
    }

    private static SimDayPending Pending(DateOnly day)
    {
        var record = new ReplaySessionRecord(day, [], [], [new ReplayCandidateRow("AAA", "BUY", "S1|1D|BUY|swing|FIXED_RR|*", 1)], [new ReplayBlocked("BBB", "S1|1D|BUY|swing|FIXED_RR|*", BlockReason.MaxPositions, "full")], [], [], 1_000_500m, 0m, 0m, 1, [], []);
        var text = ReplayJson.Canonical(record);
        return new SimDayPending(day, text, "[]", 1_000_500m, ReplayJson.Sha256(text), D2);
    }

    private async Task<T> WithStoreAsync<T>(Func<ISimDaysStore, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISimDaysStore>());
    }

    private async Task<Guid> StoredAsync()
    {
        var run = New();
        (await WithStoreAsync(s => s.InsertAsync(run, Ct).AsTask())).ShouldBeTrue();
        return run.Id;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using var response = body is null ? await Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct) : await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    [Fact]
    public async Task Every_simulation_route_needs_the_owners_session()
    {
        using var anonymous = Factory.CreateAnonymousClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync(new Uri($"/api/sim/days/{id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var read in new[] { "/api/sim/days", "/api/sim/days/settings", $"/api/sim/days/{id}/days" })
        {
            (await anonymous.GetAsync(new Uri(read, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, read);
        }

        (await anonymous.PostAsJsonAsync(new Uri("/api/sim/days", UriKind.Relative), new { start = "2026-10-05" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var action in new[] { "settle", "advance", "continue", "cancel" })
        {
            (await anonymous.PostAsync(new Uri($"/api/sim/days/{id}/{action}", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, action);
        }
    }

    [Theory]
    [InlineData("""{"start":"2026-10-05","scope":{}}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]},"capital":0}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]},"mode":"later"}""")]
    [InlineData("""{"start":"2026-10-05","scope":{"instrumentIds":["00000000-0000-0000-0000-0000000000a1"]}}""")] // no bar is stored in this database: the start is before the first bar plus the warm-up
    public async Task A_start_that_cannot_be_used_is_422_sim_invalid(string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(new Uri("/api/sim/days", UriKind.Relative), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(Ct));
        Code(await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).ShouldBe("sim.invalid");
    }

    [Fact]
    public async Task A_second_active_simulation_is_409_sim_busy_and_the_database_refuses_it_too()
    {
        await StoredAsync();

        var (status, body) = await PostAsync("/api/sim/days", new { start = "2026-10-05", scope = new { instrumentIds = new[] { Guid.NewGuid() } } });

        (status, Code(body)).ShouldBe((HttpStatusCode.Conflict, "sim.busy"));
        (await WithStoreAsync(s => s.InsertAsync(New(), Ct).AsTask())).ShouldBeFalse("the partial unique index admits one active run");
    }

    [Fact]
    public async Task Settle_advance_and_the_day_read_move_the_cursor_and_store_an_immutable_day()
    {
        var id = await StoredAsync();

        var early = await PostAsync($"/api/sim/days/{id}/advance");
        var settle = await PostAsync($"/api/sim/days/{id}/settle");
        (await WithStoreAsync(s => s.SettleAsync(id, Pending(D1), T0, Ct).AsTask())).ShouldBeTrue();
        var pending = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct);
        var again = await PostAsync($"/api/sim/days/{id}/settle");
        var advanced = await PostAsync($"/api/sim/days/{id}/advance");
        var day = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}/days/2026-10-05", UriKind.Relative), Ct);

        (early.Status, Code(early.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.not_settled"));
        (settle.Status, settle.Body.GetProperty("phase").GetString()).ShouldBe((HttpStatusCode.Accepted, "settling"));
        pending.GetProperty("stored").GetBoolean().ShouldBeFalse();
        (again.Status, Code(again.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.busy"));
        advanced.Status.ShouldBe(HttpStatusCode.OK);
        advanced.Body.GetProperty("cursor").GetString().ShouldBe("2026-10-06");
        advanced.Body.GetProperty("phase").GetString().ShouldBe("idle");
        advanced.Body.GetProperty("daysDone").GetInt32().ShouldBe(1);
        advanced.Body.GetProperty("equity").GetDecimal().ShouldBe(1_000_500m);
        advanced.Body.GetProperty("costsLabel").GetString()!.ShouldStartWith("gross");
        advanced.Body.GetProperty("hindsight").GetArrayLength().ShouldBeGreaterThan(0);
        day.GetProperty("stored").GetBoolean().ShouldBeTrue();
        day.GetProperty("picks")[0].GetProperty("symbol").GetString().ShouldBe("AAA");
        day.GetProperty("blocked")[0].GetProperty("reason").GetString().ShouldBe("max_positions");
    }

    [Fact]
    public async Task The_last_day_completes_the_run_and_continue_cancel_and_auto_answer_by_phase()
    {
        var id = await StoredAsync();
        var notComplete = await PostAsync($"/api/sim/days/{id}/continue");
        await WithStoreAsync(async s =>
        {
            foreach (var day in new[] { D1, D2 })
            {
                (await s.MovePhaseAsync(id, "idle", "settling", T0, Ct)).ShouldBeTrue();
                (await s.SettleAsync(id, Pending(day), T0, Ct)).ShouldBeTrue();
                (await PostAsync($"/api/sim/days/{id}/advance")).Status.ShouldBe(HttpStatusCode.OK);
            }

            return 0;
        });

        var status = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}", UriKind.Relative), Ct);
        var auto = await PostAsync($"/api/sim/days/{id}/auto", new { on = true });
        var cancelled = await PostAsync($"/api/sim/days/{id}/cancel");
        var twice = await PostAsync($"/api/sim/days/{id}/cancel");
        var missing = await PostAsync($"/api/sim/days/{Guid.NewGuid()}/settle");

        (notComplete.Status, Code(notComplete.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.not_complete"));
        (status.GetProperty("phase").GetString(), status.GetProperty("daysDone").GetInt32(), status.GetProperty("daysTotal").GetInt32()).ShouldBe(("complete", 2, 2));
        status.GetProperty("hasNewSessions").GetBoolean().ShouldBeFalse("no newer session is stored");
        (auto.Status, Code(auto.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.final"), "auto-run applies to an active run");
        cancelled.Body.GetProperty("phase").GetString().ShouldBe("cancelled");
        (twice.Status, Code(twice.Body)).ShouldBe((HttpStatusCode.Conflict, "sim.final"));
        (missing.Status, Code(missing.Body)).ShouldBe((HttpStatusCode.NotFound, "sim.not_found"));
    }

    [Fact]
    public async Task The_lists_page_the_runs_newest_first_and_a_runs_days_as_a_projection_and_the_settings_answer_the_defaults()
    {
        var older = New() with { CreatedAt = T0.AddDays(-1) };
        (await WithStoreAsync(s => s.InsertAsync(older, Ct).AsTask())).ShouldBeTrue();
        (await WithStoreAsync(s => s.CancelAsync(older.Id, T0, Ct).AsTask())).ShouldBeTrue();
        var id = await StoredAsync();
        await WithStoreAsync(async s =>
        {
            foreach (var day in new[] { D1, D2 })
            {
                (await s.MovePhaseAsync(id, "idle", "settling", T0, Ct)).ShouldBeTrue();
                (await s.SettleAsync(id, Pending(day), T0, Ct)).ShouldBeTrue();
                (await PostAsync($"/api/sim/days/{id}/advance")).Status.ShouldBe(HttpStatusCode.OK);
            }

            return 0;
        });

        var page1 = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/sim/days?limit=1", UriKind.Relative), Ct);
        var page2 = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/sim/days?limit=1&cursor=" + page1.GetProperty("nextCursor").GetString(), UriKind.Relative), Ct);
        var days = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/sim/days/{id}/days?sort=desc", UriKind.Relative), Ct);
        var settings = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/sim/days/settings", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri($"/api/sim/days/{Guid.NewGuid()}/days", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/sim/days?limit=0", UriKind.Relative), Ct);

        var first = page1.GetProperty("items")[0];
        (first.GetProperty("id").GetGuid(), first.GetProperty("phase").GetString(), first.GetProperty("active").GetBoolean()).ShouldBe((id, "complete", false));
        (first.GetProperty("equity").GetDecimal(), first.GetProperty("daysDone").GetInt32(), first.GetProperty("scope").GetProperty("baskets").GetInt32(), first.GetProperty("scope").GetProperty("instruments").GetInt32()).ShouldBe((1_000_500m, 2, 1, 0));
        var second = page2.GetProperty("items")[0];
        (second.GetProperty("id").GetGuid(), second.GetProperty("phase").GetString(), second.GetProperty("equity").GetDecimal()).ShouldBe((older.Id, "cancelled", 1_000_000m));
        page2.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        days.GetProperty("items").GetArrayLength().ShouldBe(2);
        var newest = days.GetProperty("items")[0];
        (newest.GetProperty("session").GetString(), newest.GetProperty("equityClose").GetDecimal(), newest.GetProperty("trades").GetInt32(), newest.GetProperty("picks").GetInt32(), newest.GetProperty("seenHoldOut").GetBoolean()).ShouldBe(("2026-10-06", 1_000_500m, 0, 1, false));
        newest.TryGetProperty("record", out _).ShouldBeFalse("a projection, never the record");
        (settings.GetProperty("defaultCapital").GetDecimal(), settings.GetProperty("speedSecondsPerMinute").GetInt32()).ShouldBe((1_000_000m, 10));
        (missing.StatusCode, Code(await missing.Content.ReadFromJsonAsync<JsonElement>(Ct))).ShouldBe((HttpStatusCode.NotFound, "sim.not_found"));
        (bad.StatusCode, Code(await bad.Content.ReadFromJsonAsync<JsonElement>(Ct))).ShouldBe((HttpStatusCode.UnprocessableEntity, "sim.invalid"));
    }

    [Fact]
    public async Task A_run_keeps_what_it_was_started_with_and_a_cancelled_run_stays_cancelled()
    {
        var id = await StoredAsync();
        await using var connection = await OpenAsync();

        await using (var rewrite = new NpgsqlCommand("UPDATE research.sim_days_runs SET capital = 5 WHERE id = @id", connection))
        {
            rewrite.Parameters.AddWithValue("id", id);
            (await Should.ThrowAsync<PostgresException>(() => rewrite.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "even the owner cannot rewrite what a run was started with (the runtime role cannot update the column at all: ResearchGrantsTests)");
        }

        (await WithStoreAsync(s => s.CancelAsync(id, T0, Ct).AsTask())).ShouldBeTrue();
        await using var reopen = new NpgsqlCommand("UPDATE research.sim_days_runs SET phase = 'idle' WHERE id = @id", connection);
        reopen.Parameters.AddWithValue("id", id);
        (await Should.ThrowAsync<PostgresException>(() => reopen.ExecuteNonQueryAsync(Ct))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation, "the trigger keeps a cancelled run final");
    }

    /// <summary>The row count of every table of the planning, paper (with shadow), trading, selection, settings, confidence, lab, portfolio, accounts and readiness schemas and of every research table (cell statistics, models) but the simulation's own.</summary>
    private async Task<Dictionary<string, long>> CountsAsync()
    {
        await using var connection = await OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname IN ('workflow', 'paper', 'trading', 'selection', 'config', 'confidence', 'research', 'lab', 'portfolio', 'accounts', 'readiness') AND tablename NOT LIKE 'sim_days_%' ORDER BY 1", connection))
        await using (var reader = await list.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM " + table, connection);
            counts[table] = (long)(await count.ExecuteScalarAsync(Ct))!;
        }

        return counts;
    }

    [Fact]
    public async Task A_settled_and_advanced_day_writes_no_planning_paper_shadow_statistics_model_or_settings_row()
    {
        await BlockAfterTheTestDaysAsync();
        Factory.ReplayStart.Gate = new CompleteBackfill();
        var id = await StoredAsync();
        var before = await CountsAsync();
        before.Keys.ShouldContain("research.cell_stats");
        before.Keys.ShouldContain("research.model_versions");
        before.Keys.ShouldContain("paper.shadow_entries");

        (await PostAsync($"/api/sim/days/{id}/settle")).Status.ShouldBe(HttpStatusCode.Accepted);
        // The real runner and engine take the manual settle (it runs in market hours too). Here it can only fail: the integration doubles cannot step a session (StubReplayFacts throws
        // on MarketAsync and SizingAsync) and seeding a whole tradable market is out of reach, so this proves a failed tick writes nothing. A successful settle is covered by
        // SimDaysRunnerTests (the real engine over fakes, writing only through ISimDaysStore) and ReplayArchitectureTests (no write outside the two tables, statically).
        await Factory.Services.GetRequiredService<SimDaysRunner>().TickAsync(Ct);
        var ticked = await WithStoreAsync(s => s.GetAsync(id, Ct).AsTask());
        ticked!.Phase.ShouldBe(SimDays.Failed, "the real runner took the settle and ran the real engine, which cannot prepare the test's empty basket: " + ticked.FailReason + ticked.Waiting);
        var after = await CountsAsync();
        after.ShouldBe(before, "the runner's tick wrote nothing outside the simulation's own tables");
        (await WithStoreAsync(s => s.CancelAsync(id, T0, Ct).AsTask())).ShouldBeFalse("an error run is final");
        id = await StoredAsync(); // a second run walks the rest of the routes over a settled day, as the runner leaves it
        (await PostAsync($"/api/sim/days/{id}/settle")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await WithStoreAsync(s => s.SettleAsync(id, Pending(D1), T0, Ct).AsTask())).ShouldBeTrue();
        (await PostAsync($"/api/sim/days/{id}/advance")).Status.ShouldBe(HttpStatusCode.OK);
        (await PostAsync($"/api/sim/days/{id}/auto", new { on = true })).Status.ShouldBe(HttpStatusCode.OK);
        (await PostAsync($"/api/sim/days/{id}/cancel")).Status.ShouldBe(HttpStatusCode.OK);

        (await CountsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task The_tape_of_a_settled_day_pages_its_bars_and_events_by_minute_and_refuses_an_unknown_run_or_an_unsettled_day()
    {
        await BlockAfterTheTestDaysAsync();
        var instrument = await SeedAsync("TAPEA");
        await using (var connection = await OpenAsync())
        {
            foreach (var (time, close) in new[] { ("09:15", 100.0), ("09:16", 101.0), ("09:17", 102.0), ("10:00", 99.0) })
            {
                var at = new DateTimeOffset(D1.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)), TimeSpan.FromMinutes(330));
                await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, @ts, @c, @c, @c, @c, 100, 'csv')", ("id", instrument), ("ts", at.ToUniversalTime()), ("c", close));
            }
        }

        var id = await StoredAsync();
        var open = new DateTimeOffset(D1.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330));
        var trade = new SimDayTrade(1, "TAPEA", "BUY", "S1|1D|BUY|swing|FIXED_RR|*", 10, D1, open, 100m, 98m, true, null, null, null, null, instrument);
        var pending = Pending(D1) with { TradesJson = ReplayJson.Canonical(new[] { trade }) };
        var unsettled = await GetAsync($"/api/sim/days/{id}/days/2026-10-05/tape");
        (await PostAsync($"/api/sim/days/{id}/settle")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await WithStoreAsync(s => s.SettleAsync(id, pending, T0, Ct).AsTask())).ShouldBeTrue();

        var page = await GetAsync($"/api/sim/days/{id}/days/2026-10-05/tape?from={Uri.EscapeDataString("2026-10-05T09:15:00+05:30")}&to={Uri.EscapeDataString("2026-10-05T09:16:00+05:30")}");
        var unknown = await GetAsync($"/api/sim/days/{Guid.NewGuid()}/days/2026-10-05/tape");
        var never = await GetAsync($"/api/sim/days/{id}/days/2026-10-20/tape");
        var off = await GetAsync($"/api/sim/days/{id}/days/2026-10-05/tape?from={Uri.EscapeDataString("2026-10-05T10:00:00+05:30")}&to={Uri.EscapeDataString("2026-10-05T09:16:00+05:30")}");

        (unsettled.Status, Code(unsettled.Body)).ShouldBe((HttpStatusCode.Conflict, SimDays.NotSettledCode));
        page.Status.ShouldBe(HttpStatusCode.OK, page.Body.ToString());
        page.Body.GetProperty("bars").GetArrayLength().ShouldBe(2, "no bar after to");
        page.Body.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ShouldBe([SimTape.EntryKind]);
        var position = page.Body.GetProperty("board").GetProperty("open").EnumerateArray().Single();
        (position.GetProperty("lastPrice").GetDecimal(), position.GetProperty("runningPnl").GetDecimal()).ShouldBe((101m, 10m));
        (unknown.Status, Code(unknown.Body)).ShouldBe((HttpStatusCode.NotFound, SimDays.NotFoundCode));
        (never.Status, Code(never.Body)).ShouldBe((HttpStatusCode.NotFound, "sim.day_not_found"));
        (off.Status, Code(off.Body)).ShouldBe((HttpStatusCode.UnprocessableEntity, SimDays.InvalidCode));
    }

    [Fact]
    public async Task A_one_minute_page_of_fifty_held_instruments_reads_only_the_day_and_answers_fast()
    {
        await BlockAfterTheTestDaysAsync();
        var instruments = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            instruments.Add(await SeedAsync($"TAPEP{i:00}"));
        }

        await using (var connection = await OpenAsync())
        {
            // a full session (375 minutes) of D2 for every instrument, and one bar of D1
            await ExecuteAsync(
                connection,
                "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) SELECT i, @d2 + m * interval '1 minute', 100 + m * 0.01, 100 + m * 0.01, 100 + m * 0.01, 100 + m * 0.01, 100, 'csv' FROM unnest(@ids) i CROSS JOIN generate_series(0, 374) m UNION ALL SELECT i, @d1, 100, 100, 100, 100, 100, 'csv' FROM unnest(@ids) i",
                ("ids", instruments.ToArray()), ("d2", new DateTimeOffset(D2.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330)).ToUniversalTime()), ("d1", new DateTimeOffset(D1.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330)).ToUniversalTime()));
        }

        var id = await StoredAsync();
        var entry = new DateTimeOffset(D1.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330));
        var held = instruments.Select((g, i) => new SimDayTrade(i + 1, $"TAPEP{i:00}", "BUY", "S1|1D|BUY|swing|FIXED_RR|*", 1, D1, entry, 100m, 95m, true, null, null, null, null, g)).ToArray();
        (await PostAsync($"/api/sim/days/{id}/settle")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await WithStoreAsync(s => s.SettleAsync(id, Pending(D1) with { TradesJson = ReplayJson.Canonical(held) }, T0, Ct).AsTask())).ShouldBeTrue();
        (await PostAsync($"/api/sim/days/{id}/advance")).Status.ShouldBe(HttpStatusCode.OK);
        (await PostAsync($"/api/sim/days/{id}/settle")).Status.ShouldBe(HttpStatusCode.Accepted);
        (await WithStoreAsync(s => s.SettleAsync(id, Pending(D2), T0, Ct).AsTask())).ShouldBeTrue();
        var minute = Uri.EscapeDataString("2026-10-06T12:00:00+05:30");
        var path = $"/api/sim/days/{id}/days/2026-10-06/tape?from={minute}&to={minute}";
        (await GetAsync(path)).Status.ShouldBe(HttpStatusCode.OK); // warm the route once

        var best = long.MaxValue; // best of three samples, so a busy gate machine does not flake the check (target < 200 ms, measured 16-21 ms)
        (HttpStatusCode Status, JsonElement Body) page = default;
        for (var sample = 0; sample < 3; sample++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var got = await GetAsync(path);
            clock.Stop();
            best = Math.Min(best, clock.ElapsedMilliseconds);
            page = got;
        }

        TestContext.Current.SendDiagnosticMessage($"SR-04 tape: one-minute page of 50 instruments in {best} ms (best of 3)");
        page.Status.ShouldBe(HttpStatusCode.OK, page.Body.ToString());
        page.Body.GetProperty("symbols").GetArrayLength().ShouldBe(50, "the trades held into the day are in play");
        page.Body.GetProperty("bars").GetArrayLength().ShouldBe(50, "one minute: one bar each");
        var board = page.Body.GetProperty("board");
        board.GetProperty("open").GetArrayLength().ShouldBe(50);
        board.GetProperty("equityOpen").GetDecimal().ShouldBe(1_000_500m, "the close of the stored day before");
        best.ShouldBeLessThan(200, "spec SR-04 non-functional: a one-minute page in < 200 ms");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}
