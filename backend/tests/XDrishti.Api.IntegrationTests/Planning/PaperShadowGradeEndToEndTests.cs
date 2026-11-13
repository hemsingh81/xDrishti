using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Paper;
using XDrishti.Application.Planning;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// CF-08 part B, criterion 5 on the real path (real simulator, stores and fabricated bars; the clocks are pinned and the database clock <c>paper.shadow_now()</c> is replaced by the owner role for the test and restored after): one daily ticket is shadowed in the evening before
/// its session, approved, armed and expired, then captured by the paper capture, graded by P7-13 and graded by the shadow grader. The three agree to the last digit (R, entry and exit instant, exit reason, engine), the shadow row and the paper entry carry one trade key, and the
/// summary shows the behavioural and the edge count side by side.
/// <code>
/// swing "DAILY_UP" (buy when close &gt; sma(3), stop = lowest low of 3 days, target 2R), the days of the March fixture of GradeEndToEndTests moved to January 2099:
///   Jan 5-12 flat 100 | Jan 13 flat 110 (signal at its close) | Jan 14 (the session F) opens 110, spikes to 135 at 10:00 (target 130) | Jan 15 flat 135
/// </code>
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperShadowGradeEndToEndTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string DailyUp = """
        strategy:
          schema: 1
          id: DAILY_UP
          style: swing
          timeframes:
            - 1D
          sides:
            - BUY
          params:
            rr: 2
          trigger:
            all:
              - "close > sma(3)"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: 3
          exits:
            - policy: FIXED_RR
              rr: [rr]
          time_rules:
            max_hold_sessions: 5
        """;

    private const string SwingCell = "DAILY_UP|1D|BUY|swing|FIXED_RR{rr=2}|*";

    private static readonly Guid AccountId = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7a06");

    private static readonly DateOnly D = new(2099, 1, 13);

    private static readonly DateOnly F = new(2099, 1, 14);

    private static readonly int[] FlatDays = [5, 6, 7, 8, 9, 12];

    private static DateOnly Jan(int day) => new(2099, 1, day);

    private static DateTimeOffset Utc(int day, int hour, int minute) => new(2099, 1, day, hour, minute, 0, TimeSpan.Zero);

    private static Bar[] Session(DateOnly date, double price, int? spikeAt = null, double spikeHigh = 0)
    {
        var bars = new List<Bar>();
        var level = price;
        for (var m = 0; m < SessionClock.RegularBars; m++)
        {
            var epoch = Day.Epoch(date, Day.Minute("09:15") + m);
            if (m == spikeAt)
            {
                bars.Add(new Bar(epoch, level, spikeHigh, level, spikeHigh, Day.MinuteVolume));
                level = spikeHigh;
            }
            else
            {
                bars.Add(new Bar(epoch, level, level, level, level, Day.MinuteVolume));
            }
        }

        return [.. bars];
    }

    private async Task SetDatabaseClockAsync(DateTimeOffset? at)
    {
        var body = at is { } t ? $"SELECT '{t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}+00'::timestamptz" : "SELECT clock_timestamp()";
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, $"CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql VOLATILE AS $fn$ {body} $fn$");
    }

    private async Task PinAsync(DateTimeOffset at)
    {
        Factory.Clock.Pin(at);
        await SetDatabaseClockAsync(at);
    }

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_shadow_grade_equals_the_P7_13_grade_and_the_P10_paper_trade_of_the_same_ticket_and_the_summary_shows_both_counts()
    {
        try
        {
            await using (var connection = await OpenAsync())
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                    VALUES (@id, 'probe-shadow-grade', 'Probe', 'Dhan', '1000000082', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    ("id", AccountId));
            }

            // the bars, the strategy and the basket
            var bars = ReplayDays.Join(
                [.. FlatDays.Select(d => Session(Jan(d), 100)), Session(Jan(13), 110), Session(Jan(14), 110, 45, 135), Session(Jan(15), 135)]);
            var instrument = await SeedAsync("SHG");
            await using (var connection = await OpenAsync())
            {
                await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", instrument));
            }

            using (var scope = Factory.Services.CreateScope())
            {
                await using var merge = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(instrument, BarSource.Dhan, Ct);
                await merge.AddAsync(bars, Ct);
                await merge.CompleteAsync(null, Ct);
            }

            using var created = await Client.PostJsonAsync("/api/baskets", new { name = "shadow grading" }, Ct);
            var basket = (await created.Content.ReadFromJsonAsync<XDrishti.Application.Market.Baskets.BasketView>(Ct))!.Id;
            using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = new[] { instrument } }, Ct);
            added.EnsureSuccessStatusCode();
            var definition = StrategyFile.Read(DailyUp).Definition!;
            var canonical = CanonicalStrategy.Json(definition);
            var hash = CanonicalText.Sha256Hex(canonical);
            await WithAsync(async p => (await p.GetRequiredService<IStrategyStore>().AppendAsync(new NewVersion("DAILY_UP", "swing", DailyUp, canonical, hash, Seed: true), Ct)).IsSuccess);

            // the evening of D: the plan for F is stored, the owner switches the record on, the writer shadows it before the open of F
            await PinAsync(Utc(13, 15, 40));
            var item = new NewPlanItem(
                1, TicketGroup.Primary, "NSE:SHG", "BUY", "DAILY_UP", hash, "t", "s", "g", "v", [], new PlanEvidence(SwingCell, 40, "r", "b", null, null, [], null), [new NewAllocation(AccountId, "paper", 100)]);
            var plan = new NewPlan("shadow-e2e", F, Utc(14, 3, 35), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [item]);
            var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
            var itemId = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).Single().Id;
            await using (var connection = await OpenAsync())
            {
                await ExecuteAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'owner:test@example.com', 'test')");
                await ExecuteAsync(
                    connection,
                    "INSERT INTO paper.evidence_sets (edge_epoch, versions, set_hash, mode, actor, reason) VALUES ((SELECT COALESCE(max(id), 0) FROM paper.epochs), to_jsonb(ARRAY[@hash::text]), repeat('d', 64), 'owner', 'owner:test@example.com', 'test')",
                    ("hash", hash)); // AM-01b: the evidence set of the epoch in force names the version of the plan item
            }

            var written = await WithAsync(p => p.GetRequiredService<ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>>().HandleAsync(new WriteShadowCommand(null), Ct).AsTask());
            written.Value.ShouldBe(new ShadowWriteOutcome(true, 1, 1, 1, 0, 0));

            // the owner approves, the pre-open service arms at 09:10, the expiry service expires after the close
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(itemId, TicketState.Approved, "owner:test@example.com", "approved for watching"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<MovePlanCommand, PlanMoveOutcome>>().HandleAsync(new MovePlanCommand(published.Id, PlanState.Approved, "system:test", "approved"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
            await PinAsync(Utc(14, 3, 40));
            (await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(itemId, TicketState.Approved, TicketState.Armed, "system:preopen", "armed", null), Ct).AsTask())).ShouldBe(MoveStatus.Created);
            await PinAsync(Utc(14, 11, 30));
            (await WithAsync(p => p.GetRequiredService<IPlanStore>().MoveItemAsync(new ItemMove(itemId, TicketState.Armed, TicketState.Expired, "system:plan-expiry", "expired", null), Ct).AsTask())).ShouldBe(MoveStatus.Created);

            // the next morning F has closed: paper capture, the P7-13 grade and the shadow grade each replay the ticket
            await PinAsync(Utc(15, 6, 30));
            await WithAsync(p => p.GetRequiredService<IPaperStore>().EnsureFirstEpochAsync(F, Utc(15, 6, 30), Ct).AsTask());
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<CapturePaperFillsCommand, PaperCaptureOutcome>>().HandleAsync(new CapturePaperFillsCommand(null, true), Ct).AsTask())).Value.EntriesWritten.ShouldBe(1);
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<GradePlansCommand, GradeOutcome>>().HandleAsync(new GradePlansCommand(F), Ct).AsTask())).Value.Graded.ShouldBe(1);
            var graded = await WithAsync(p => p.GetRequiredService<ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>>().HandleAsync(new GradeShadowCommand(), Ct).AsTask());
            var again = await WithAsync(p => p.GetRequiredService<ICommandHandler<GradeShadowCommand, ShadowGradeOutcome>>().HandleAsync(new GradeShadowCommand(), Ct).AsTask());

            graded.Value.Graded.ShouldBe(1);
            again.Value.Seen.ShouldBe(0); // a graded entry is not replayed or rewritten
            var p713 = (await WithAsync(p => p.GetRequiredService<IGradeStore>().ForPlansAsync([published.Id], Ct).AsTask())).Values.Single().Grade.Grade;
            var shadowR = await ScalarAsync<double>("SELECT r_sim FROM paper.shadow_grades");
            var paperR = await ScalarAsync<double>("SELECT x.r_sim FROM paper.exits x JOIN paper.entries e ON e.id = x.entry_id");
            p713.R.ShouldNotBeNull().ShouldBeGreaterThan(1.0);
            shadowR.ShouldBe(p713.R!.Value); // to the last digit
            shadowR.ShouldBe(paperR);
            (await ScalarAsync<string>("SELECT r_basis FROM paper.shadow_grades")).ShouldBe("gross");
            (await ScalarAsync<string>("SELECT exit_reason FROM paper.shadow_grades")).ShouldBe(p713.ExitReason);
            (await ScalarAsync<DateTime>("SELECT entry_at FROM paper.shadow_grades")).ShouldBe(await ScalarAsync<DateTime>("SELECT entry_at FROM paper.entries"));
            (await ScalarAsync<DateTime>("SELECT exit_at FROM paper.shadow_grades")).ShouldBe(await ScalarAsync<DateTime>("SELECT exit_at FROM paper.exits"));
            (await ScalarAsync<string>("SELECT engine_version FROM paper.shadow_grades")).ShouldBe(await ScalarAsync<string>("SELECT engine_version FROM paper.entries"));
            (await ScalarAsync<string>("SELECT trade_key FROM paper.shadow_entries")).ShouldBe(await ScalarAsync<string>("SELECT trade_key FROM paper.entries")); // the two records join on one key
            (await ScalarAsync<string>("SELECT trade_key FROM paper.shadow_entries")).ShouldBe("DAILY_UP|NSE:SHG|Buy|2099-01-13");

            // the summary shows the P10 count and the edge count side by side
            await ResignInAsync();
            var summary = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/shadow/summary", UriKind.Relative), Ct);
            summary.GetProperty("enabled").GetBoolean().ShouldBeTrue();
            summary.GetProperty("hasRows").GetBoolean().ShouldBeTrue();
            var compare = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/paper/backtest-vs-paper", UriKind.Relative), Ct);
            summary.GetProperty("counts").GetProperty("behavioural").GetInt32().ShouldBe(compare.GetProperty("counts").GetProperty("counted").GetInt32()); // P10's own count of the same epoch, not touched by the shadow trade
            summary.GetProperty("counts").GetProperty("edgeRaw").GetInt32().ShouldBe(1);
            summary.GetProperty("statement").GetString().ShouldBe("modelled fills; no queue, no latency");
            summary.GetProperty("groups")[0].GetProperty("meanR").GetProperty("number").GetDouble().ShouldBe(shadowR);
            summary.GetProperty("costs").GetProperty("gross").GetBoolean().ShouldBeTrue();
            summary.GetProperty("chain").GetProperty("sessions").GetInt32().ShouldBe(1);
        }
        finally
        {
            await SetDatabaseClockAsync(null);
        }
    }
}
