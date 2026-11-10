using System.Text;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets.Systems;

public sealed class RunBasketFetchHandlerTests
{
    private readonly FetchWorld _w = new();

    private static string Problem(FetchOutcomeView outcome) => $"{outcome.Outcome}:{outcome.Error}";

    [Fact]
    public async Task With_the_setting_off_nothing_is_asked_and_no_request_is_made()
    {
        _w.Settings.Enabled = false;
        _w.Serve(FetchWorld.Csv(1, 50));

        var result = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:system-baskets"), FetchWorld.Ct);

        result.Error.Code.ShouldBe(BasketErrors.FetchDisabledCode);
        (_w.Fetcher.Calls, _w.Store.ListCalls, _w.Store.Recorded.Count, _w.Applier.Applied.Count).ShouldBe((0, 0, 0, 0));
    }

    [Fact]
    public async Task The_scheduled_service_ends_as_skipped_disabled_while_the_setting_is_off()
    {
        _w.Settings.Enabled = false;
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(XDrishti.Application.Abstractions.Messaging.ICommandHandler<RunBasketFetchCommand, BasketFetchRun>)).Returns(_w.Handler);
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        var service = new SystemBasketsService(factory);
        var context = new XDrishti.Application.Abstractions.Platform.ServiceRunContext(Guid.NewGuid(), XDrishti.Domain.Platform.Services.ServiceRunTrigger.Scheduled, 1, "system:test", new Dictionary<string, System.Text.Json.JsonElement>(), (_, _) => ValueTask.CompletedTask);

        var outcome = await service.RunAsync(context, FetchWorld.Ct);

        (outcome.Status, outcome.Message).ShouldBe((XDrishti.Domain.Platform.Services.ServiceRunStatus.Skipped, "disabled"));
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_user_agent_refuses_to_run()
    {
        _w.Settings.UserAgent = " ";

        var result = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);

        result.Error.Code.ShouldBe(BasketErrors.InvalidCode);
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_source_that_is_not_enabled_is_not_fetched_by_the_schedule()
    {
        _w.Store.Sources[0] = _w.Source(enabled: false);

        var result = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);

        result.Value.Outcomes.ShouldBeEmpty();
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_first_fetch_adds_every_member_from_the_india_date_of_the_fetch()
    {
        _w.Serve(FetchWorld.Csv(1, 50));

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.Added, outcome.Removed, outcome.Rows, outcome.Unmatched).ShouldBe(("applied", 50, 0, 50, 0));
        var applied = _w.Applier.Applied.Single();
        applied.Change.Added.Count.ShouldBe(50);
        applied.EffectiveFrom.ShouldBe(new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.FromMinutes(330)));
        applied.DetectedAt.ShouldBe(_w.Clock.GetUtcNow());
        applied.Source.State.ShouldBe("ok");
        applied.Source.Sha256.ShouldBe(FetchWorld.Sha(FetchWorld.Csv(1, 50)));
    }

    [Fact]
    public async Task The_same_bytes_again_are_unchanged_and_write_no_member()
    {
        var csv = FetchWorld.Csv(1, 50);
        _w.Members(1, 50);
        _w.Store.Sources[0] = _w.Source(sha: FetchWorld.Sha(csv));
        _w.Serve(csv);

        var outcome = await _w.RunAsync();

        outcome.Outcome.ShouldBe("unchanged");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_304_answer_is_unchanged_and_keeps_the_stored_hash()
    {
        _w.Members(1, 50);
        _w.Store.Sources[0] = _w.Source(sha: "abc", etag: "\"v1\"");
        _w.Fetcher.Response = new SourceResponse(304, null, null, null, null, null);

        var outcome = await _w.RunAsync();

        outcome.Outcome.ShouldBe("unchanged");
        _w.Applier.Applied.ShouldBeEmpty();
        _w.Store.Recorded.Single().Source.Sha256.ShouldBe("abc");
    }

    [Fact]
    public async Task A_changed_file_with_the_same_members_is_unchanged()
    {
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(1, 50).Replace("Misc", "Other", StringComparison.Ordinal));

        var outcome = await _w.RunAsync();

        outcome.Outcome.ShouldBe("unchanged");
        _w.Applier.Applied.ShouldBeEmpty();
        _w.Store.Recorded.Single().Source.Sha256.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_review_week_swap_of_two_is_applied_with_the_fetch_date_as_the_effective_date()
    {
        _w.On(2026, 10, 1);
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(3, 50));

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.Added, outcome.Removed).ShouldBe(("applied", 2, 2));
        _w.Applier.Applied.Single().EffectiveFrom.ShouldBe(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task Outside_a_review_window_two_out_and_two_in_is_within_the_bound_of_three()
    {
        _w.On(2026, 11, 10);
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(3, 50));

        (await _w.RunAsync()).Outcome.ShouldBe("applied");
    }

    [Fact]
    public async Task Outside_a_review_window_four_out_and_four_in_is_rejected_and_nothing_is_applied()
    {
        _w.On(2026, 11, 10);
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(5, 50));

        var outcome = await _w.RunAsync();

        Problem(outcome).ShouldBe("rejected:too_many_changes");
        _w.Applier.Applied.ShouldBeEmpty();
        _w.Store.Recorded.Single().Source.State.ShouldBe("rejected");
    }

    [Fact]
    public async Task Removing_thirty_of_fifty_trips_the_wire_unless_the_owner_forces_it()
    {
        _w.On(2026, 10, 1); // inside the window: only the tripwire is in play
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(31, 50));

        Problem(await _w.RunAsync()).ShouldBe("rejected:tripwire");
        _w.Applier.Applied.ShouldBeEmpty();

        var forced = await _w.RunAsync(force: true);

        (forced.Outcome, forced.Forced, forced.Removed).ShouldBe(("applied", true, 30));
    }

    [Fact]
    public async Task A_forced_fetch_stores_the_setting_snapshot_and_the_actor()
    {
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(1, 50));
        _w.Store.Sources[0] = _w.Source(sha: "old");

        await _w.RunAsync(force: true);

        _w.Store.Recorded.Count.ShouldBe(1);
        var fetch = _w.Store.Recorded[0].Fetch;
        (fetch.Forced, fetch.Actor, fetch.SettingsSnapshot).ShouldBe((true, "owner:test@example.invalid", "{}"));
    }

    [Theory]
    [InlineData(404, "text/html", "<html>Not Found</html>", "http_404")]
    [InlineData(200, "text/html", "<html>Maintenance</html>", "not_csv")]
    [InlineData(200, "application/json", "{}", "not_csv")]
    public async Task A_page_that_is_not_the_file_is_rejected_and_the_old_membership_stays(int status, string contentType, string body, string reason)
    {
        _w.Members(1, 50);
        _w.Serve(body, status, contentType);

        Problem(await _w.RunAsync()).ShouldBe($"rejected:{reason}");
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_header_is_rejected()
    {
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(1, 50).Replace("ISIN Code", "ISIN", StringComparison.Ordinal));

        Problem(await _w.RunAsync()).ShouldBe("rejected:bad_header");
    }

    [Fact]
    public async Task Forty_nine_rows_of_a_fifty_row_index_are_rejected()
    {
        _w.Members(1, 50);
        _w.Serve(FetchWorld.Csv(1, 49));

        Problem(await _w.RunAsync()).ShouldBe("rejected:row_count");
    }

    [Theory]
    [InlineData(10, "applied")]
    [InlineData(11, "rejected")]
    public async Task More_than_a_fifth_of_rows_without_a_master_match_is_rejected(int unmatched, string outcome)
    {
        _w.Serve(FetchWorld.Csv(1, 50, unmatched));

        (await _w.RunAsync()).Outcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task A_row_matches_by_isin_even_when_the_symbol_was_renamed()
    {
        _w.Serve(FetchWorld.Csv(1, 50).Replace("S001", "NEWNAME", StringComparison.Ordinal));

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.Unmatched).ShouldBe(("applied", 0));
        _w.Applier.Applied.Single().Change.Added.ShouldContain(_w.InstrumentIds[0]);
        _w.Store.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_row_with_an_unknown_isin_matches_by_symbol_with_a_warning()
    {
        _w.Serve(FetchWorld.Csv(1, 50).Replace(FetchWorld.Isin(1), "INE999Z01010", StringComparison.Ordinal));

        await _w.RunAsync();

        var fetch = _w.Applier.Applied.Single().Fetch;
        fetch.Unmatched.ShouldBe(0);
        fetch.WarningsJson.ShouldContain("matched_by_symbol:S001");
        _w.Applier.Applied.Single().Change.Added.ShouldContain(_w.InstrumentIds[0]);
    }

    [Fact]
    public async Task A_row_matching_neither_is_listed_as_unmatched_and_is_not_a_member()
    {
        _w.Serve(FetchWorld.Csv(1, 50, unmatched: 2));

        var outcome = await _w.RunAsync();

        outcome.Unmatched.ShouldBe(2);
        var applied = _w.Applier.Applied.Single();
        applied.Change.Added.Count.ShouldBe(48);
        applied.Fetch.WarningsJson.ShouldContain("unmatched:GHOST0");
    }

    [Fact]
    public async Task A_retired_instrument_that_is_still_listed_stays_a_member_with_a_flag()
    {
        var retired = _w.Store.Master[0] with { RetiredAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) };
        _w.Store.Master[0] = retired;
        _w.Serve(FetchWorld.Csv(1, 50));

        await _w.RunAsync();

        var applied = _w.Applier.Applied.Single();
        applied.Change.Added.ShouldContain(retired.Id);
        applied.Fetch.WarningsJson.ShouldContain("retired:S001:2026-09-01");
    }

    [Fact]
    public async Task A_member_the_master_retired_stays_while_the_file_still_lists_it()
    {
        _w.Members(1, 50);
        _w.Store.Master[0] = _w.Store.Master[0] with { RetiredAt = _w.Clock.GetUtcNow() }; // retired in the master, still listed
        _w.On(2026, 10, 1);
        _w.Serve(FetchWorld.Csv(1, 50));

        (await _w.RunAsync()).Outcome.ShouldBe("unchanged");
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task A_403_or_429_fails_and_rests_the_source_for_24_hours(int status)
    {
        _w.Fetcher.Response = new SourceResponse(status, "text/html", [], null, null, null);

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.HttpStatus).ShouldBe(("failed", status));
        _w.Store.Recorded.Single().Source.NextAttemptAt.ShouldBe(_w.Clock.GetUtcNow().AddHours(24));
        _w.Applier.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_source_that_rests_is_not_asked_by_the_schedule_but_is_by_the_owner()
    {
        _w.Store.Sources[0] = _w.Source(next: _w.Clock.GetUtcNow().AddHours(5));
        _w.Fetcher.Response = SourceResponse.Unreachable("timeout");

        var scheduled = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);
        var manual = await _w.Handler.HandleAsync(new RunBasketFetchCommand([_w.BasketId], false, "owner:x"), FetchWorld.Ct);

        scheduled.Value.Outcomes.ShouldBeEmpty();
        manual.Value.Outcomes.Single().Outcome.ShouldBe("failed");
        _w.Fetcher.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_fails_retries_in_five_minutes_and_keeps_the_last_good_validators()
    {
        _w.Store.Sources[0] = _w.Source(sha: "good", etag: "\"v0\"", lastOk: _w.Clock.GetUtcNow().AddDays(-1));
        _w.Fetcher.Response = SourceResponse.Unreachable("timeout");

        var outcome = await _w.RunAsync();

        outcome.Error.ShouldBe("timeout");
        var update = _w.Store.Recorded.Single().Source;
        (update.State, update.NextAttemptAt, update.Sha256, update.Etag, update.FailCount).ShouldBe(("failed", _w.Clock.GetUtcNow().AddMinutes(5), "good", "\"v0\"", 1));
    }

    [Fact]
    public async Task After_three_days_without_a_good_check_the_source_is_stale()
    {
        _w.Store.Sources[0] = _w.Source(lastOk: _w.Clock.GetUtcNow().AddDays(-4));
        _w.Fetcher.Response = SourceResponse.Unreachable("timeout");

        await _w.RunAsync();

        _w.Store.Recorded.Single().Source.State.ShouldBe("stale");
    }

    [Fact]
    public async Task Fetch_now_never_turns_a_source_on_and_asks_nothing_of_a_disabled_one()
    {
        _w.Store.Sources[0] = _w.Source(enabled: false);
        _w.Serve(FetchWorld.Csv(1, 50));

        var result = await _w.Handler.HandleAsync(new RunBasketFetchCommand([_w.BasketId], false, "owner:x"), FetchWorld.Ct);

        result.Value.Outcomes.ShouldBeEmpty();
        (_w.Fetcher.Calls, _w.Store.Sources[0].Enabled, _w.Store.Recorded.Count).ShouldBe((0, false, 0));
    }

    [Fact]
    public async Task A_failure_is_retried_after_5_then_30_then_120_minutes_and_then_not_that_day()
    {
        _w.Fetcher.Response = SourceResponse.Unreachable("timeout");
        var waits = new List<TimeSpan?>();
        BasketSourceInfo source = _w.Source();
        for (var i = 0; i < 4; i++)
        {
            _w.Store.Sources[0] = source;
            await _w.RunAsync(manual: true);
            var update = _w.Store.Recorded[^1].Source;
            waits.Add(update.NextAttemptAt - _w.Clock.GetUtcNow());
            source = source with { NextAttemptAt = update.NextAttemptAt, FailCount = update.FailCount, LastCheckedAt = update.LastCheckedAt };
        }

        waits.ShouldBe([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), null]);
    }

    [Fact]
    public async Task The_schedule_asks_a_source_once_a_day_from_the_run_time()
    {
        var now = _w.Clock.GetUtcNow();
        _w.Fetcher.Response = new SourceResponse(304, null, null, null, null, null);

        _w.Store.Sources[0] = _w.Source(checkedAt: now.AddHours(-1)); // checked today already
        var done = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);
        _w.Store.Sources[0] = _w.Source(checkedAt: now.AddDays(-1));
        var due = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);
        _w.On(2026, 10, 23, 6, 30); // before the run time
        _w.Store.Sources[0] = _w.Source(checkedAt: now.AddDays(-1));
        var early = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);

        (done.Value.Outcomes.Count, due.Value.Outcomes.Count, early.Value.Outcomes.Count).ShouldBe((0, 1, 0));
    }

    [Fact]
    public async Task In_a_review_window_a_source_is_asked_twice_a_day()
    {
        _w.On(2026, 9, 25);
        var now = _w.Clock.GetUtcNow();
        _w.Fetcher.Response = new SourceResponse(304, null, null, null, null, null);
        _w.Store.Sources[0] = _w.Source(checkedAt: now.AddHours(-1));
        var soon = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);
        _w.Store.Sources[0] = _w.Source(checkedAt: now.AddHours(-13).AddMinutes(-1));
        _w.Clock.Advance(TimeSpan.FromHours(1));
        var later = await _w.Handler.HandleAsync(new RunBasketFetchCommand(null, false, "system:x"), FetchWorld.Ct);

        (soon.Value.Outcomes.Count, later.Value.Outcomes.Count).ShouldBe((0, 1));
    }

    [Fact]
    public async Task The_preferred_host_b_asks_the_alternate_url_first()
    {
        var alt = "https://niftyindices.com/IndexConstituent/ind_nifty50list.csv";
        _w.Store.Sources[0] = _w.Source(alt: alt);
        _w.Settings.PreferredHost = "B";
        _w.Fetcher.Response = new SourceResponse(304, null, null, null, null, null);

        await _w.RunAsync();

        _w.Fetcher.LastAsked!.SourceUrl.ShouldBe(alt);
    }

    [Fact]
    public async Task A_lost_race_with_a_concurrent_change_is_a_failed_fetch_with_nothing_counted()
    {
        _w.Applier.Succeeds = false;
        _w.Serve(FetchWorld.Csv(1, 50));

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.Error, outcome.Added).ShouldBe(("failed", "concurrent_change", 0));
    }

    [Fact]
    public async Task A_file_with_a_bom_blank_lines_and_a_repeated_symbol_still_applies()
    {
        var csv = "﻿" + FetchWorld.Csv(1, 50) + "\r\n\r\n" + "Dup,Misc,S001,EQ," + FetchWorld.Isin(1) + "\r\n";
        _w.Fetcher.Response = new SourceResponse(200, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv), null, null, null);

        var outcome = await _w.RunAsync();

        (outcome.Outcome, outcome.Rows).ShouldBe(("applied", 50));
    }
}
