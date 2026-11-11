using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-05: the list baskets (flags read from the master, the NSE ban file, the NSE lot file) on a real PostgreSQL; the files are served from memory and the network is never reached.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ListBasketsApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly Dictionary<string, Guid> _shares = [];

    private static DateTimeOffset At(int month, int day, int hour = 9, int minute = 0) => new DateTimeOffset(2026, month, day, hour, minute, 0, Ist).ToUniversalTime();

    private static DateTimeOffset Midnight(int month, int day) => new DateTimeOffset(2026, month, day, 0, 0, 0, Ist);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedAsync();
        Factory.Clock.Pin(At(10, 25));
        Owner = await Factory.SignInElevatedAsync(Ct);
    }

    private async Task SeedAsync()
    {
        await Factory.WithDbAsync(async db =>
        {
            // L001..L005: L001 F&O with an ASM flag Y; L002 F&O; L004 F&O; L005 F&O (retired later); L003 plain, series BE.
            await AddShareAsync(db, "L001", fno: true, asm: "N", category: null, series: "EQ", mtf: null);
            await AddShareAsync(db, "L002", fno: true, asm: "S", category: "GSM Stage 2", series: "EQ", mtf: 4m);
            await AddShareAsync(db, "L003", fno: false, asm: "N", category: null, series: "BE", mtf: 0m);
            await AddShareAsync(db, "L004", fno: true, asm: "N", category: null, series: "EQ", mtf: 3m);
            await AddShareAsync(db, "L005", fno: true, asm: "N", category: null, series: "EQ", mtf: null);
            await AddFutureAsync(db, "L001", 250);
            await AddFutureAsync(db, "L002", 100);
            foreach (var definition in ListBaskets.Baskets.Append(ListBaskets.LotSizes))
            {
                var isLots = definition.Key == ListBaskets.LotSizes.Key;
                var url = definition.Format switch
                {
                    ListBaskets.BanFormat => "https://nsearchives.nseindia.com/content/fo/fo_secban.csv",
                    ListBaskets.LotsFormat => "https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv",
                    _ => string.Empty,
                };
                var file = definition.Format switch { ListBaskets.BanFormat => "fo_secban.csv", ListBaskets.LotsFormat => "fo_mktlots.csv", _ => "instrument-master" };
                var enabled = definition.Format == ListBaskets.MasterFlagsFormat;
                var (min, max) = definition.Format switch { ListBaskets.BanFormat => (0, 200), ListBaskets.LotsFormat => (2, 10), _ => (0, 100000) };
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, archived_at, revision, kind, system)
                    VALUES ({definition.Id}, {definition.Name}, '#64748b', 'watch', {definition.Description}, 0, now(), {(isLots ? DateTimeOffset.UtcNow : (DateTimeOffset?)null)}, 0, 'list', true)
                    """);
                await db.Database.ExecuteSqlAsync($"""
                    INSERT INTO market.basket_sources (basket_id, source_url, file_name, host_alt, format_version, cadence, expected_min, expected_max, enabled, state, fail_count)
                    VALUES ({definition.Id}, {url}, {file}, NULL, {definition.Format}, 'x', {min}, {max}, {enabled}, 'never', 0)
                    """);
            }

            return 0;
        });
    }

    private async Task AddShareAsync(XDrishtiDbContext db, string symbol, bool fno, string asm, string? category, string series, decimal? mtf)
    {
        var id = Guid.CreateVersion7();
        _shares[symbol] = id;
        var canonical = "NSE:" + symbol;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, series, fno_eligible, asm_gsm_flag, asm_gsm_category, mtf_leverage, first_seen_at, last_seen_at)
            VALUES ({id}, {canonical}, 'NSE', 'NSE_EQ', 'share', {canonical}, {canonical}, 1, {symbol}, {series}, {fno}, {asm}, {category}, {mtf}, now(), now())
            """);
    }

    private static async Task AddFutureAsync(XDrishtiDbContext db, string underlying, int lot)
    {
        var id = Guid.CreateVersion7();
        var canonical = $"NSE:{underlying}-OCT2026-FUT";
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, expiry_date, fno_eligible, first_seen_at, last_seen_at)
            VALUES ({id}, {canonical}, 'NSE', 'NSE_FNO', 'future', {canonical}, {canonical}, {lot}, {underlying}, date '2026-10-27', false, now(), now())
            """);
    }

    private async Task MoveToAsync(DateTimeOffset instant)
    {
        Factory.Clock.Pin(instant);
        Owner = await Factory.SignInElevatedAsync(Ct); // the session expires with the clock
    }

    private async Task RefreshListsAsync(bool masterOk = true, Guid? refreshId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RefreshFlagListsCommand, FlagListRun>>();
        var result = await handler.HandleAsync(new RefreshFlagListsCommand(masterOk, refreshId ?? Guid.CreateVersion7(), "system:daily-refresh"), Ct);
        result.IsSuccess.ShouldBeTrue();
    }

    private Task<int> SqlAsync(FormattableString sql) => Factory.WithDbAsync(async db => await db.Database.ExecuteSqlAsync(sql));

    private Task<List<BasketMember>> StaysAsync(Guid basketId) => Factory.WithDbAsync(db => db.BasketMembers.AsNoTracking().Where(m => m.BasketId == basketId).ToListAsync(Ct));

    private async Task<string[]> SymbolsAsync(Guid basketId, string? asOf = null)
    {
        using var response = await Client.GetAsync($"/api/baskets/{basketId}/members" + (asOf is null ? string.Empty : $"?asOf={asOf}"), Ct);
        return [.. (await response.ReadAsync<BasketMembers>(Ct)).Items.Select(i => i.Instrument.Symbol).Order(StringComparer.Ordinal)];
    }

    private async Task<FetchOutcomeView> FetchListAsync(Guid basketId)
    {
        using var on = await Client.PutJsonAsync($"/api/baskets/{basketId}/source", new { enabled = true }, Ct);
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await Client.PostJsonAsync("/api/baskets/system/fetch", new { basketIds = new[] { basketId } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadAsync<BasketFetchRun>(Ct)).Outcomes.Single();
    }

    private Task<string> WarningsAsync(Guid basketId) =>
        Factory.WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT warnings::text AS \"Value\" FROM market.basket_fetches WHERE basket_id = {basketId} ORDER BY fetched_at DESC, id DESC LIMIT 1").SingleAsync(Ct));

    // ---- master-derived lists (AC 1, 2, 3, 11, 12, 13) ----

    [Fact]
    public async Task The_master_lists_are_dated_from_each_refresh_with_provenance_and_closed_never_deleted_and_no_request_is_made()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await RefreshListsAsync(refreshId: first);
        (await SymbolsAsync(ListBaskets.FnoStocks.Id)).ShouldBe(["NSE:L001", "NSE:L002", "NSE:L004", "NSE:L005"]);
        (await SymbolsAsync(ListBaskets.AsmGsm.Id)).ShouldBe(["NSE:L002"]);
        (await SymbolsAsync(ListBaskets.Mtf.Id)).ShouldBe(["NSE:L002", "NSE:L004"]); // L003's 0 and L001's null are not eligible
        (await SymbolsAsync(ListBaskets.TradeForTrade.Id)).ShouldBe(["NSE:L003"]);

        await MoveToAsync(At(10, 26, 8, 46));
        await SqlAsync($"UPDATE market.instruments SET fno_eligible = true WHERE canonical_symbol = 'NSE:L003'"); // one flag turned on
        await SqlAsync($"UPDATE market.instruments SET fno_eligible = false WHERE canonical_symbol = 'NSE:L004'"); // one turned off
        await SqlAsync($"UPDATE market.instruments SET retired_at = now() WHERE canonical_symbol = 'NSE:L005'"); // one retired
        await SqlAsync($"UPDATE market.instruments SET asm_gsm_flag = 'Y', asm_gsm_category = 'ASM Stage 1' WHERE canonical_symbol = 'NSE:L001'"); // N to a flag
        await RefreshListsAsync(refreshId: second);

        var fno = await StaysAsync(ListBaskets.FnoStocks.Id);
        var added = fno.Single(m => m.InstrumentId == _shares["L003"]);
        (added.ValidFrom, added.ValidTo, added.Provenance, added.SourceKind, added.SourceRef).ShouldBe((Midnight(10, 26), null, "derived", "refresh", second));
        fno.Single(m => m.InstrumentId == _shares["L004"]).ValidTo.ShouldBe(Midnight(10, 26));
        fno.Single(m => m.InstrumentId == _shares["L005"]).ValidTo.ShouldBe(Midnight(10, 26)); // retired: closed, the row stays
        fno.Single(m => m.InstrumentId == _shares["L001"]).SourceRef.ShouldBe(first);
        var asm = await StaysAsync(ListBaskets.AsmGsm.Id);
        asm.Single(m => m.InstrumentId == _shares["L001"]).Note.ShouldBe("ASM Stage 1");
        asm.Single(m => m.InstrumentId == _shares["L002"]).Note.ShouldBe("GSM Stage 2");
        (await SymbolsAsync(ListBaskets.FnoStocks.Id, "2026-10-25")).ShouldBe(["NSE:L001", "NSE:L002", "NSE:L004", "NSE:L005"]); // point in time: what was flagged then
        (await SymbolsAsync(ListBaskets.FnoStocks.Id, "2026-10-26")).ShouldBe(["NSE:L001", "NSE:L002", "NSE:L003"]);
        (await SymbolsAsync(ListBaskets.AsmGsm.Id, "2026-10-25")).ShouldBe(["NSE:L002"]);
        Factory.BasketSources.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_past_date_before_the_first_refresh_gets_the_first_known_members_and_the_date_since_known()
    {
        await RefreshListsAsync();

        using var response = await Client.GetAsync($"/api/baskets/{ListBaskets.FnoStocks.Id}/members?asOf=2020-01-01", Ct);
        var early = await response.ReadAsync<BasketMembers>(Ct);

        early.Items.Count.ShouldBe(4);
        early.MembersKnownSince.ShouldBe(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public async Task A_failed_master_run_keeps_the_members_and_the_source_shows_failed()
    {
        await RefreshListsAsync();
        await MoveToAsync(At(10, 26, 8, 46));
        await SqlAsync($"UPDATE market.instruments SET fno_eligible = false");

        await RefreshListsAsync(masterOk: false);

        (await SymbolsAsync(ListBaskets.FnoStocks.Id)).Length.ShouldBe(4);
        var list = (await ListBasketsAsync()).Single(b => b.Id == ListBaskets.FnoStocks.Id);
        list.Source!.State.ShouldBe("failed");
    }

    [Fact]
    public async Task The_lists_appear_as_system_list_baskets_every_change_route_is_409_copy_out_is_allowed_and_no_mis_basket_exists()
    {
        await RefreshListsAsync();
        var manual = await CreateBasketAsync("Mine");

        var listed = (await ListBasketsAsync()).Where(b => b.System).ToList();

        listed.Select(b => b.Name).Order().ShouldBe(ListBaskets.Baskets.Select(b => b.Name).Order());
        listed.ShouldAllBe(b => b.Kind == "list");
        listed.ShouldNotContain(b => b.Name.Contains("MIS", StringComparison.OrdinalIgnoreCase));
        listed.ShouldNotContain(b => b.Name.Contains("lot sizes", StringComparison.OrdinalIgnoreCase)); // the lot file's source row is archived
        var id = ListBaskets.FnoStocks.Id;
        var one = _shares["L001"];
        (await SendAsync(HttpMethod.Put, $"/api/baskets/{id}", new { name = "X" })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var add = await SendAsync(HttpMethod.Post, $"/api/baskets/{id}/members", new { instrumentIds = new[] { one } });
        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{id}/members", new { instrumentIds = new[] { one } })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var copy = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = id, toBasketId = manual.Id, instrumentIds = new[] { one }, mode = "copy" });
        using var move = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = id, toBasketId = manual.Id, instrumentIds = new[] { one }, mode = "move" });

        add.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(add)).Code.ShouldBe("baskets.system");
        (copy.StatusCode, move.StatusCode).ShouldBe((HttpStatusCode.OK, HttpStatusCode.Conflict));
    }

    // ---- the ban list (AC 4, 5, 11) ----

    private static string Ban(string date, params string[] symbols) =>
        $"Securities in Ban For Trade Date {date}:\r\n" + string.Concat(symbols.Select((s, i) => $"{i + 1},{s}\r\n"));

    [Fact]
    public async Task The_ban_list_is_dated_by_the_trade_date_closes_the_previous_day_and_the_same_bytes_are_unchanged()
    {
        Factory.BasketSources.FetchEnabled = true;
        await MoveToAsync(At(10, 26, 19, 0));
        Factory.BasketSources.Serve("fo_secban.csv", Ban("27-OCT-2026", "L001", "L002", "GHOSTCO"));

        var first = await FetchListAsync(ListBaskets.Ban.Id);

        (first.Outcome, first.Added, first.Unmatched).ShouldBe(("applied", 2, 1));
        var stays = await StaysAsync(ListBaskets.Ban.Id);
        stays.ShouldAllBe(m => m.ValidFrom == Midnight(10, 27) && m.Provenance == "fetched" && m.SourceKind == "fetch");
        (await WarningsAsync(ListBaskets.Ban.Id)).ShouldContain("unmatched:GHOSTCO");

        await MoveToAsync(At(10, 27, 8, 30)); // the morning retry of the same bytes
        var again = await FetchListAsync(ListBaskets.Ban.Id);
        again.Outcome.ShouldBe("unchanged");

        await MoveToAsync(At(10, 27, 19, 0));
        Factory.BasketSources.Serve("fo_secban.csv", Ban("28-OCT-2026", "L002"));
        var next = await FetchListAsync(ListBaskets.Ban.Id);

        (next.Outcome, next.Added, next.Removed).ShouldBe(("applied", 0, 1));
        (await StaysAsync(ListBaskets.Ban.Id)).Single(m => m.InstrumentId == _shares["L001"]).ValidTo.ShouldBe(Midnight(10, 28)); // the boundary day
        (await SymbolsAsync(ListBaskets.Ban.Id, "2026-10-27")).ShouldBe(["NSE:L001", "NSE:L002"]);
        (await SymbolsAsync(ListBaskets.Ban.Id, "2026-10-28")).ShouldBe(["NSE:L002"]);
    }

    [Fact]
    public async Task A_file_without_a_parseable_date_is_rejected_and_an_empty_file_closes_the_list()
    {
        Factory.BasketSources.FetchEnabled = true;
        await MoveToAsync(At(10, 26, 19, 0));
        Factory.BasketSources.Serve("fo_secban.csv", Ban("27-OCT-2026", "L001"));
        await FetchListAsync(ListBaskets.Ban.Id);

        Factory.BasketSources.Serve("fo_secban.csv", "Securities in Ban For Trade Date tomorrow:\r\n1,L001\r\n");
        var bad = await FetchListAsync(ListBaskets.Ban.Id);
        (await SymbolsAsync(ListBaskets.Ban.Id)).ShouldBe(["NSE:L001"]);

        Factory.BasketSources.Serve("fo_secban.csv", "Securities in Ban For Trade Date 27-OCT-2026:\r\n");
        var empty = await FetchListAsync(ListBaskets.Ban.Id);

        (bad.Outcome, bad.Error).ShouldBe(("rejected", "bad_date"));
        empty.Outcome.ShouldBe("applied");
        (await SymbolsAsync(ListBaskets.Ban.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_lists_setting_off_the_ban_file_is_not_requested()
    {
        Factory.BasketSources.Serve("fo_secban.csv", Ban("27-OCT-2026", "L001"));
        using var on = await Client.PutJsonAsync($"/api/baskets/{ListBaskets.Ban.Id}/source", new { enabled = true }, Ct);

        using var response = await Client.PostJsonAsync("/api/baskets/system/fetch", new { basketIds = new[] { ListBaskets.Ban.Id } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("baskets.fetch_disabled");
        Factory.BasketSources.Calls.ShouldBe(0);
        (await StaysAsync(ListBaskets.Ban.Id)).ShouldBeEmpty();
    }

    // ---- the lot sizes (BK-05 AC 7, 8, 9; BK-05b): the fetch record and the nse_mktlots rows of options.lot_sizes ----

    private static string Lots(params string[] rows) =>
        "UNDERLYING      ,SYMBOL    ,OCT-26   ,NOV-26   ,\r\n" + string.Concat(rows.Select(r => r + "\r\n"));

    [Fact]
    public async Task The_lot_file_is_recorded_with_a_summary_and_the_mismatch_and_universe_warnings_and_the_master_lot_is_untouched()
    {
        await RefreshListsAsync(); // the master's F&O shares: L001, L002, L004, L005
        Factory.BasketSources.FetchEnabled = true;
        await MoveToAsync(At(10, 26, 8, 50));
        Factory.BasketSources.Serve("fo_mktlots.csv", Lots("NIFTY BANK     , BANKNIFTY, 30        , 30        ,", "L001 CO         , L001     , 275       , 275       ,", "L002 CO         , L002     , 100       , 100       ,"));

        var outcome = await FetchListAsync(ListBaskets.LotSizes.Id);

        outcome.Outcome.ShouldBe("applied"); // the two share lots are new rows of options.lot_sizes (BK-05b)
        var warnings = await WarningsAsync(ListBaskets.LotSizes.Id);
        warnings.ShouldContain("lotsWritten=2");
        warnings.ShouldContain("indexUnmatched=1"); // BANKNIFTY is not an index of this master
        (await LotRowsAsync()).ShouldBe([("L001", 275, "2026-10-26"), ("L002", 100, "2026-10-26")]);
        warnings.ShouldContain("lots_summary:shares=2,nearest=OCT-26,mismatches=1");
        warnings.ShouldContain("lot_size_mismatch:L001:master=250:nse=275");
        warnings.ShouldNotContain("lot_size_mismatch:L002");
        warnings.ShouldContain("index_rows_skipped:1");
        warnings.ShouldContain("fo_universe_diff:onlyMaster=2,onlyNse=0");
        (await Factory.WithDbAsync(db => db.Instruments.AsNoTracking().Where(i => i.Type == "future" && i.UnderlyingSymbol == "L001").Select(i => i.LotSize).SingleAsync(Ct))).ShouldBe(250);
        (await SymbolsAsync(ListBaskets.FnoStocks.Id)).Length.ShouldBe(4); // the membership is not corrected
        (await Factory.WithDbAsync(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name = 'fo_lot_sizes'").SingleAsync(Ct))).ShouldBe(0);
    }

    private async Task<List<(string Underlying, int Lot, string From)>> LotRowsAsync(string source = "nse_mktlots") =>
        [.. (await Factory.WithDbAsync(db => db.Database.SqlQuery<string>(
            $"SELECT i.underlying_symbol || '|' || l.lot_size || '|' || l.effective_from || '|' || l.confirmed || '|' || l.source || '|' || coalesce(l.source_url, '') AS \"Value\" FROM options.lot_sizes l JOIN market.instruments i ON i.id = l.underlying_id WHERE l.source = {source} ORDER BY i.underlying_symbol, l.id").ToListAsync(Ct)))
            .Select(r => r.Split('|')).Select(c =>
            {
                c[3].ShouldBe("false"); // never confirmed
                c[4].ShouldBe("nse_mktlots");
                c[5].ShouldBe("https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv"); // the provenance
                return (c[0], int.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), c[2]);
            })];

    private async Task FetchLotsOnAsync(int day, params string[] rows)
    {
        await MoveToAsync(At(10, day, 8, 50));
        Factory.BasketSources.Serve("fo_mktlots.csv", Lots(rows));
        (await FetchListAsync(ListBaskets.LotSizes.Id)).Outcome.ShouldNotBe("failed");
    }

    private static string Row(string symbol, int lot, int next) => $"{symbol} CO         , {symbol,-8}   , {lot,-9}   , {next,-9}   ,";

    [Fact]
    public async Task A_lot_is_written_again_only_when_it_changes_and_each_row_is_dated_by_its_fetch_day_and_never_updated()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;

        await FetchLotsOnAsync(26, Row("L001", 275, 275), Row("L002", 100, 100));
        await FetchLotsOnAsync(27, Row("L001", 275, 300), Row("L002", 100, 100)); // the same lots (only a later column moved): no row
        (await LotRowsAsync()).Count.ShouldBe(2);
        await FetchLotsOnAsync(28, Row("L001", 475, 475), Row("L002", 100, 100));

        (await LotRowsAsync()).ShouldBe([("L001", 275, "2026-10-26"), ("L001", 475, "2026-10-28"), ("L002", 100, "2026-10-26")]);
        (await Factory.WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT warnings::text AS \"Value\" FROM market.basket_fetches WHERE basket_id = {ListBaskets.LotSizes.Id} AND outcome = 'unchanged' ORDER BY fetched_at LIMIT 1").SingleAsync(Ct))).ShouldContain("lotsWritten=0");
    }

    [Fact]
    public async Task An_nse_lot_equal_to_the_lot_in_force_from_any_source_is_not_written_and_a_different_one_is_and_the_confirmed_lot_still_wins_the_lookup()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;
        var l001 = _shares["L001"];
        await SqlAsync($"INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed) VALUES ({l001}, date '2026-09-26', 65, 'circular', 'https://nseindia.test/circular', true)");

        await FetchLotsOnAsync(26, Row("L001", 65, 65), Row("L002", 100, 100)); // L001 equals the confirmed circular lot: no row; L002 has none: a row
        (await LotRowsAsync()).ShouldBe([("L002", 100, "2026-10-26")]);
        var rows = await Factory.WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT lot_size || '|' || confirmed || '|' || source AS \"Value\" FROM options.lot_sizes WHERE underlying_id = {l001} ORDER BY id").ToListAsync(Ct));
        rows.ShouldBe(["65|true|circular"]);

        await FetchLotsOnAsync(27, Row("L001", 75, 75), Row("L002", 100, 100)); // a different lot: a row, the circular one stays
        (await LotRowsAsync()).ShouldBe([("L001", 75, "2026-10-27"), ("L002", 100, "2026-10-26")]);
    }

    [Fact]
    public async Task The_same_bytes_twice_are_unchanged_and_write_no_lot_row()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;
        await FetchLotsOnAsync(26, Row("L001", 275, 275), Row("L002", 100, 100));

        await MoveToAsync(At(10, 27, 8, 50));
        var second = await FetchListAsync(ListBaskets.LotSizes.Id);

        second.Outcome.ShouldBe("unchanged");
        (await LotRowsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_index_row_is_written_when_its_canonical_symbol_names_one_index_of_the_master()
    {
        await RefreshListsAsync();
        var nifty = Guid.CreateVersion7();
        await SqlAsync($"""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES ({nifty}, 'NSE:NIFTY', 'NSE', 'IDX_I', 'index', 'NIFTY', 'Nifty 50', 1, 'NIFTY', false, now(), now())
            """);
        Factory.BasketSources.FetchEnabled = true;

        await FetchLotsOnAsync(26, Row("NIFTY", 65, 65), Row("BANKNIFTY", 30, 30), Row("L001", 250, 250), Row("L002", 100, 100));

        (await LotRowsAsync()).ShouldBe([("L001", 250, "2026-10-26"), ("L002", 100, "2026-10-26"), ("NIFTY", 65, "2026-10-26")]);
        (await WarningsAsync(ListBaskets.LotSizes.Id)).ShouldContain("indexUnmatched=1");
    }

    [Fact]
    public async Task A_rejected_file_writes_nothing_and_the_earlier_rows_stand()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;
        await FetchLotsOnAsync(26, Row("L001", 275, 275), Row("L002", 100, 100));
        await MoveToAsync(At(10, 27, 8, 50));
        Factory.BasketSources.Serve("fo_mktlots.csv", Lots(Row("L001", 999, 999).Replace("999", "many     "), Row("L002", 101, 101)));

        var outcome = await FetchListAsync(ListBaskets.LotSizes.Id);

        (outcome.Outcome, outcome.Error).ShouldBe(("rejected", "bad_lot"));
        (await LotRowsAsync()).ShouldBe([("L001", 275, "2026-10-26"), ("L002", 100, "2026-10-26")]);
    }

    [Fact]
    public async Task With_the_lists_setting_off_the_lot_file_is_not_requested_and_no_lot_row_is_written()
    {
        await RefreshListsAsync();
        Factory.BasketSources.Serve("fo_mktlots.csv", Lots(Row("L001", 275, 275), Row("L002", 100, 100)));
        using var on = await Client.PutJsonAsync($"/api/baskets/{ListBaskets.LotSizes.Id}/source", new { enabled = true }, Ct);

        using var response = await Client.PostJsonAsync("/api/baskets/system/fetch", new { basketIds = new[] { ListBaskets.LotSizes.Id } }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        Factory.BasketSources.Calls.ShouldBe(0);
        (await LotRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_after_the_lot_rows_are_inserted_rolls_back_the_rows_and_the_fetch_record()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;
        await FetchLotsOnAsync(26, Row("L001", 275, 275), Row("L002", 100, 100));
        var taken = await Factory.WithDbAsync(db => db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM market.basket_fetches WHERE basket_id = {ListBaskets.LotSizes.Id} LIMIT 1").SingleAsync(Ct));
        var fetches = await Factory.WithDbAsync(db => db.BasketFetches.CountAsync(Ct));
        using var scope = Factory.Services.CreateScope();
        var applier = scope.ServiceProvider.GetRequiredService<INseLotApplier>();
        var now = At(10, 29, 8, 50);
        var source = new SourceUpdate(ListBaskets.LotSizes.Id, true, "ok", now, now, null, null, null, null, 0);
        var clash = new FetchRecord(taken, ListBaskets.LotSizes.Id, now, 200, null, null, null, 2, "applied", 0, 0, 0, "[]", null, false, "system:test", null); // the id of a stored fetch: the insert of the row fails after the lots

        var written = await applier.ApplyAsync(new NseLotApplication([new NseLot(_shares["L001"], 999), new NseLot(_shares["L002"], 998)], new DateOnly(2026, 10, 29), "https://example.test/f.csv", source, _ => clash), Ct);

        written.ShouldBeNull();
        (await LotRowsAsync()).Count.ShouldBe(2); // only the two rows of the first day
        (await Factory.WithDbAsync(db => db.BasketFetches.CountAsync(Ct))).ShouldBe(fetches);
    }

    [Fact]
    public async Task A_lot_row_of_nse_is_a_row_beside_the_master_lot_and_never_changes_the_instrument_table()
    {
        await RefreshListsAsync();
        Factory.BasketSources.FetchEnabled = true;

        await FetchLotsOnAsync(26, Row("L001", 275, 275), Row("L002", 100, 100));

        (await Factory.WithDbAsync(db => db.Instruments.AsNoTracking().Where(i => i.Type == "future" && i.UnderlyingSymbol == "L001").Select(i => i.LotSize).SingleAsync(Ct))).ShouldBe(250);
        (await Factory.WithDbAsync(db => db.Instruments.AsNoTracking().Where(i => i.Type == "share" && i.UnderlyingSymbol == "L001").Select(i => i.LotSize).SingleAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task A_lot_file_with_a_bad_lot_is_rejected()
    {
        Factory.BasketSources.FetchEnabled = true;
        await MoveToAsync(At(10, 26, 8, 50));
        Factory.BasketSources.Serve("fo_mktlots.csv", Lots("L001 CO         , L001     , many      , 275       ,", "L002 CO         , L002     , 100       , 100       ,"));

        var outcome = await FetchListAsync(ListBaskets.LotSizes.Id);

        (outcome.Outcome, outcome.Error).ShouldBe(("rejected", "bad_lot"));
    }

    [Fact]
    public async Task The_archived_lot_source_basket_is_not_listed_and_cannot_be_copied_from()
    {
        var manual = await CreateBasketAsync("Mine");

        (await ListBasketsAsync()).ShouldNotContain(b => b.Id == ListBaskets.LotSizes.Id);
        using var copy = await SendAsync(HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = ListBaskets.LotSizes.Id, toBasketId = manual.Id, instrumentIds = new[] { _shares["L001"] }, mode = "copy" });

        copy.IsSuccessStatusCode.ShouldBeFalse();
        (await MemberIdsAsync(manual.Id)).ShouldBeEmpty();
    }

    // ---- the flags of basket 13 (AC 13 of BK-03, wired by BK-05) ----

    private async Task SeedBarsAsync() => await SqlAsync($"""
        INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete)
        SELECT i.id, '1D', (d::timestamp AT TIME ZONE 'Asia/Kolkata'), 100, 101, 99, 100, 1000, 375, true
        FROM market.instruments i, generate_series(date '2026-10-01', date '2026-10-23', interval '1 day') AS d WHERE i.type = 'share' AND extract(dow FROM d) NOT IN (0, 6)
        """);

    /// <summary>The three lists were first checked on 20 Oct; the master lists last on <paramref name="masterLast"/>, the ban list's newest trade date is <paramref name="banThrough"/>; L002 is under surveillance from 22 to 25 Oct and banned from 24 Oct.</summary>
    private async Task SeedListsAsync(DateTimeOffset masterLast, DateOnly banThrough)
    {
        foreach (var list in new[] { ListBaskets.AsmGsm.Id, ListBaskets.Ban.Id, ListBaskets.TradeForTrade.Id })
        {
            foreach (var at in new[] { At(10, 20), list == ListBaskets.Ban.Id ? At(10, 20) : masterLast })
            {
                await SqlAsync($"""
                    INSERT INTO market.basket_fetches (id, basket_id, fetched_at, rows, outcome, added, removed, unmatched, warnings, forced, actor)
                    VALUES ({Guid.CreateVersion7()}, {list}, {at}, 0, 'unchanged', 0, 0, 0, '[]', false, 'seed')
                    """);
            }
        }

        await SqlAsync($"UPDATE market.basket_sources SET known_through = {banThrough} WHERE basket_id = {ListBaskets.Ban.Id}");
        var (from22, to25, from24) = (Midnight(10, 22).ToUniversalTime(), Midnight(10, 25).ToUniversalTime(), Midnight(10, 24).ToUniversalTime());
        await SqlAsync($"""
            INSERT INTO market.basket_members (id, basket_id, instrument_id, position, valid_from, valid_to, added_by, removed_by, provenance, source_kind)
            VALUES ({Guid.CreateVersion7()}, {ListBaskets.AsmGsm.Id}, {_shares["L002"]}, 0, {from22}, {to25}, 'seed', 'seed', 'derived', 'refresh'),
                   ({Guid.CreateVersion7()}, {ListBaskets.Ban.Id}, {_shares["L002"]}, 0, {from24}, NULL, 'seed', NULL, 'fetched', 'fetch')
            """);
    }

    private async Task<DayFlag[]> FlagsOfAsync(string symbol)
    {
        using var scope = Factory.Services.CreateScope();
        var universe = await scope.ServiceProvider.GetRequiredService<IDerivedUniverseReader>().ReadAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 23), Ct);
        return universe.Single(u => u.InstrumentId == _shares[symbol]).Bars.Flags!;
    }

    [Fact]
    public async Task The_universe_reader_has_no_flags_until_the_three_lists_are_recorded_and_then_the_dated_state_up_to_the_last_good_check()
    {
        await SeedBarsAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var before = await scope.ServiceProvider.GetRequiredService<IDerivedUniverseReader>().ReadAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 23), Ct);
            before.ShouldNotBeEmpty();
            before.ShouldAllBe(u => u.Bars.Flags == null); // a missing flag is not a pass
        }

        await SeedListsAsync(At(10, 25), new DateOnly(2026, 10, 25));

        (await FlagsOfAsync("L002")).Select(f => (f.Date.Day, f.AsmGsm, f.InBan, f.Series, f.Unknown)).ShouldBe(
            [(21, false, false, "EQ", false), (22, true, false, "EQ", false), (24, true, true, "EQ", false), (25, false, true, "EQ", false), (26, false, false, "", true)]);
        (await FlagsOfAsync("L003")).Where(f => !f.Unknown).Select(f => (f.Date.Day, f.Series)).ShouldBe([(21, "EQ")]); // BE now, but the list has no stay for it: it was EQ on every recorded date
    }

    [Fact]
    public async Task A_ban_list_whose_newest_trade_date_is_behind_leaves_the_later_days_unknown()
    {
        await SeedBarsAsync();
        await SeedListsAsync(At(10, 25), new DateOnly(2026, 10, 23)); // fetched for 23 Oct, the fetch for 24 Oct failed

        (await FlagsOfAsync("L002")).Select(f => (f.Date.Day, f.AsmGsm, f.InBan, f.Unknown)).ShouldBe([(21, false, false, false), (22, true, false, false), (24, false, false, true)]);
    }

    [Fact]
    public async Task A_failed_master_run_leaves_the_days_after_the_last_good_check_unknown()
    {
        await SeedBarsAsync();
        await SeedListsAsync(At(10, 22), new DateOnly(2026, 10, 25)); // the master lists were last checked on 22 Oct

        (await FlagsOfAsync("L002")).Select(f => (f.Date.Day, f.AsmGsm, f.InBan, f.Unknown)).ShouldBe([(21, false, false, false), (22, true, false, false), (23, false, false, true)]);
    }

    [Fact]
    public async Task The_ban_source_knows_its_newest_trade_date_and_a_failed_fetch_does_not_move_it()
    {
        Factory.BasketSources.FetchEnabled = true;
        await MoveToAsync(At(10, 26, 19, 0));
        Factory.BasketSources.Serve("fo_secban.csv", Ban("27-OCT-2026", "L001"));
        await FetchListAsync(ListBaskets.Ban.Id);
        await MoveToAsync(At(10, 27, 19, 0));
        Factory.BasketSources.Serve("fo_secban.csv", "<html>busy</html>", 500, "text/html");

        var failed = await FetchListAsync(ListBaskets.Ban.Id);

        failed.Outcome.ShouldBe("failed");
        (await Factory.WithDbAsync(db => db.BasketSources.AsNoTracking().Where(x => x.BasketId == ListBaskets.Ban.Id).Select(x => x.KnownThrough).SingleAsync(Ct))).ShouldBe(new DateOnly(2026, 10, 27));
    }
}
