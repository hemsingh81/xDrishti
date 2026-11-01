using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Market;

namespace XDrishti.Api.IntegrationTests.OptionContracts;

/// <summary>
/// Spec P14-01 criteria 1 to 4 through the real refresh and TimescaleDB: the master sample with option rows copied from Dhan's real file (2026-11-01) appended;
/// the contracts of the listed underlyings are kept, the master list is what it was, a re-import changes nothing, a later file advances <c>last_seen_on</c> only,
/// a contract missing from the file keeps its row, and a lot change is one dated <c>scrip_master</c> row.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionImportTests(ApiFactory factory) : MarketTestBase(factory)
{
    private const string NiftyWeeklyCall = "NSE,D,35008,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-CE,NIFTY 09 NOV 17350 CALL,OP,NA,65.0,2026-11-09,17350.00000,CE,5.0000,W,N,N,N,NA,A,0,0,0,0,0,0,0,6313.0000,5153.9000,1756,";
    private const string NiftyWeeklyPut = "NSE,D,35009,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-PE,NIFTY 09 NOV 17350 PUT,OP,NA,65.0,2026-11-09,17350.00000,PE,5.0000,W,N,N,N,NA,A,0,0,0,0,0,0,0,20.0500,0.0500,1756,";
    private const string NiftyMonthlyCall = "NSE,D,35062,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-CE,NIFTY 23 NOV 17350 CALL,OP,NA,65.0,2026-11-23,17350.00000,CE,5.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,6355.3500,5196.3500,1756,";
    private const string BankNiftyCall = "NSE,D,35000,NA,OPTIDX,26009,BANKNIFTY,BANKNIFTY-Dec2026-69400-CE,BANKNIFTY 29 DEC 69400 CALL,OP,NA,30.0,2026-12-29,69400.00000,CE,5.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,22.0000,0.0500,901,";
    private const string CromptonCall = "NSE,D,100325,NA,OPTSTK,17094,CROMPTON,CROMPTON-Nov2026-147.5-CE,CROMPTON 23 NOV 147.50 CALL,OP,NA,2150.0,2026-11-23,147.50000,CE,1.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,64.6200,24.6200,116101,";

    private string Sample { get; } = FakeMasterData.Fixture("dhan-master-sample.csv").TrimEnd('\n', '\r');

    private string MasterWith(params string[] rows) => Sample + "\n" + string.Join('\n', rows) + "\n";

    private async Task SetUnderlyingsAsync(string json) =>
        await ExecuteAsync("INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('options.underlyings', @v, 'owner@example.invalid', now())", ("v", json));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<string>> RowsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private Task<List<string>> ContractsAsync() =>
        RowsAsync("SELECT concat_ws('|', c.security_id, i.canonical_symbol, c.instrument, c.expiry, c.strike_px, c.side, coalesce(c.expiry_kind, 'null'), c.lot_size_seen, coalesce(c.tick_px::text, 'null'), c.first_seen_on, c.last_seen_on) FROM options.contracts c JOIN market.instruments i ON i.id = c.underlying_id ORDER BY c.security_id, c.expiry");

    private Task<List<string>> LotsAsync() =>
        RowsAsync("SELECT concat_ws('|', i.canonical_symbol, l.effective_from, l.lot_size, l.source, l.source_url IS NOT NULL, l.confirmed) FROM options.lot_sizes l JOIN market.instruments i ON i.id = l.underlying_id ORDER BY i.canonical_symbol, l.effective_from, l.source");

    private Task<List<string>> InstrumentsAsync() =>
        RowsAsync("SELECT concat_ws('|', canonical_symbol, type, lot_size, coalesce(expiry_date::text, 'null'), retired_at IS NULL) FROM market.instruments ORDER BY canonical_symbol");

    [Fact]
    public async Task The_contracts_of_the_listed_underlyings_are_kept_and_the_master_list_and_its_warnings_are_what_they_were()
    {
        var master = MasterWith(NiftyWeeklyCall, NiftyWeeklyPut, NiftyMonthlyCall, BankNiftyCall, CromptonCall);
        Factory.MasterData.MasterCsv = master;
        await SetUnderlyingsAsync("[]");
        var plainRun = await RefreshAsync();
        var plain = await InstrumentsAsync();
        (await ContractsAsync()).ShouldBeEmpty("an empty list keeps no contract");
        await Factory.ResetInstrumentsAsync();
        await SetUnderlyingsAsync("""["NSE:NIFTY", "NSE:BANKNIFTY", "NSE:RELIANCE", "NSE:FINNIFTY"]""");
        Factory.MasterData.MasterCsv = master;

        var run = await RefreshAsync();

        run.Status.ShouldBe("succeeded");
        (await InstrumentsAsync()).ShouldBe(plain);
        (run.Added, run.TotalActive, run.SkippedByScope, run.RowsRead).ShouldBe((plainRun.Added, plainRun.TotalActive, plainRun.SkippedByScope, plainRun.RowsRead));
        (run.Added, run.TotalActive, run.SkippedByScope).ShouldBe((62, 62, 15 + 5));
        run.Warnings.ShouldBe(plainRun.Warnings, "a normal option import adds no warning; its counts go to the log");
        (await ContractsAsync()).ShouldBe(
        [
            "35000|NSE:BANKNIFTY|OPTIDX|2026-12-29|6940000|CE|month|30|5|2026-10-11|2026-10-11",
            "35008|NSE:NIFTY|OPTIDX|2026-11-09|1735000|CE|week|65|5|2026-10-11|2026-10-11",
            "35009|NSE:NIFTY|OPTIDX|2026-11-09|1735000|PE|week|65|5|2026-10-11|2026-10-11",
            "35062|NSE:NIFTY|OPTIDX|2026-11-23|1735000|CE|month|65|5|2026-10-11|2026-10-11",
            "106474|NSE:RELIANCE|OPTSTK|2026-10-27|72000|CE|month|500|5|2026-10-11|2026-10-11",
            "106475|NSE:RELIANCE|OPTSTK|2026-10-27|72000|PE|month|500|5|2026-10-11|2026-10-11",
            "106476|NSE:RELIANCE|OPTSTK|2026-10-27|76000|CE|month|500|5|2026-10-11|2026-10-11",
        ]);
        (await LotsAsync()).ShouldBe(
        [
            "NSE:BANKNIFTY|2025-12-30|30|secondary|t|f",
            "NSE:BANKNIFTY|2026-10-11|30|scrip_master|f|f",
            "NSE:NIFTY|2024-11-20|75|secondary|t|f",
            "NSE:NIFTY|2025-12-30|65|secondary|t|f",
            "NSE:NIFTY|2026-10-11|65|scrip_master|f|f",
            "NSE:RELIANCE|2026-10-11|500|scrip_master|f|f",
        ]);
        (await RowsAsync("SELECT value::text FROM platform.app_settings WHERE key = 'options.underlyings'")).ShouldBe(["""["NSE:NIFTY", "NSE:BANKNIFTY", "NSE:RELIANCE", "NSE:FINNIFTY"]"""], "the owner's list is never rewritten");
    }

    [Fact]
    public async Task A_reimport_changes_nothing_a_later_file_advances_last_seen_only_keeps_a_missing_contract_and_dates_a_lot_change()
    {
        await SetUnderlyingsAsync("""["NSE:NIFTY", "NSE:RELIANCE"]""");
        Factory.MasterData.MasterCsv = MasterWith(NiftyWeeklyCall, NiftyWeeklyPut, NiftyMonthlyCall);
        await RefreshAsync();
        var contracts = await ContractsAsync();
        var lots = await LotsAsync();

        var again = await RefreshAsync(force: true);

        (await ContractsAsync()).ShouldBe(contracts);
        (await LotsAsync()).ShouldBe(lots);
        again.Warnings.ShouldBeEmpty();

        // The next day: the weekly put is gone from the file and the Nifty lot reads 75 on every row (the stored rows keep what they saw).
        Factory.Clock.Pin(Now.AddDays(1));
        await ResignInAsync();
        Factory.MasterData.MasterCsv = MasterWith(NiftyWeeklyCall.Replace(",65.0,", ",75.0,", StringComparison.Ordinal), NiftyMonthlyCall.Replace(",65.0,", ",75.0,", StringComparison.Ordinal));
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        var later = await RefreshAsync();

        later.Status.ShouldBe("succeeded");
        var after = await ContractsAsync();
        after.Count.ShouldBe(6);
        after.ShouldContain("35008|NSE:NIFTY|OPTIDX|2026-11-09|1735000|CE|week|65|5|2026-10-11|2026-10-12");
        after.ShouldContain("35009|NSE:NIFTY|OPTIDX|2026-11-09|1735000|PE|week|65|5|2026-10-11|2026-10-11", "a contract missing from the file keeps its row");
        after.ShouldContain("106474|NSE:RELIANCE|OPTSTK|2026-10-27|72000|CE|month|500|5|2026-10-11|2026-10-12");
        (await LotsAsync()).Except(lots).ShouldBe(["NSE:NIFTY|2026-10-12|75|scrip_master|f|f"], "one dated row for the change, none for the unchanged RELIANCE lot");
        later.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_or_retired_underlying_is_the_one_kind_of_warning_a_list_adds()
    {
        await SetUnderlyingsAsync("""["NSE:NIFTY", "NSE:NOSUCH"]""");
        Factory.MasterData.MasterCsv = MasterWith(NiftyWeeklyCall);

        var run = await RefreshAsync();

        run.Warnings.ShouldHaveSingleItem().ShouldBe("Option underlyings not in the master list as an active index or share (1, unmapped_underlying 0 rows): NSE:NOSUCH.");
    }

    [Fact]
    public async Task A_reused_security_id_never_rewrites_or_advances_the_stored_contract()
    {
        await SetUnderlyingsAsync("""["NSE:NIFTY"]""");
        Factory.MasterData.MasterCsv = MasterWith(NiftyWeeklyCall, NiftyMonthlyCall);
        await RefreshAsync();

        // The next day Dhan's id 35008 names a contract of the same expiry with another strike, and id 35062 one of a later expiry.
        Factory.Clock.Pin(Now.AddDays(1));
        await ResignInAsync();
        var sameExpiryOtherStrike = NiftyWeeklyCall.Replace(",17350.00000,", ",18000.00000,", StringComparison.Ordinal);
        var laterExpiry = NiftyMonthlyCall.Replace(",2026-11-23,", ",2026-12-29,", StringComparison.Ordinal);
        Factory.MasterData.MasterCsv = MasterWith(sameExpiryOtherStrike, laterExpiry);
        Factory.MasterData.MasterETag = "\"dhan-v2\"";
        var later = await RefreshAsync();

        later.Status.ShouldBe("succeeded");
        (await ContractsAsync()).ShouldBe(
        [
            "35008|NSE:NIFTY|OPTIDX|2026-11-09|1735000|CE|week|65|5|2026-10-11|2026-10-11",
            "35062|NSE:NIFTY|OPTIDX|2026-11-23|1735000|CE|month|65|5|2026-10-11|2026-10-11",
            "35062|NSE:NIFTY|OPTIDX|2026-12-29|1735000|CE|month|65|5|2026-10-12|2026-10-12",
        ]);
        later.Warnings.ShouldHaveSingleItem().ShouldContain("2 Dhan security ids now name a different contract than the stored one (reused_id)");

        // A day later the same file again: the new-expiry row a reuse added is already stored as itself, so it is advanced and no longer reported.
        Factory.Clock.Pin(Now.AddDays(2));
        await ResignInAsync();
        Factory.MasterData.MasterCsv = MasterWith(laterExpiry);
        Factory.MasterData.MasterETag = "\"dhan-v3\"";
        var third = await RefreshAsync();

        third.Warnings.ShouldBeEmpty();
        (await ContractsAsync()).ShouldContain("35062|NSE:NIFTY|OPTIDX|2026-12-29|1735000|CE|month|65|5|2026-10-12|2026-10-13");
    }

    [Fact]
    public async Task The_tables_refuse_a_zero_strike_a_bad_side_and_an_unconfirmed_source_marked_confirmed()
    {
        await SetUnderlyingsAsync("""["NSE:NIFTY"]""");
        Factory.MasterData.MasterCsv = MasterWith(NiftyWeeklyCall);
        await RefreshAsync();
        const string Insert = "INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, lot_size_seen, first_seen_on, last_seen_on) SELECT 1, id, 'OPTIDX', '2026-11-09', {0}, '{1}', 65, '2026-10-11', '2026-10-11' FROM market.instruments WHERE canonical_symbol = 'NSE:NIFTY'";

        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 0, "CE")))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 100, "XX")))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, 1_735_000, "CE")))).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync("INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed) SELECT id, '2026-01-01', 65, 'secondary', 'https://example.invalid', true FROM market.instruments WHERE canonical_symbol = 'NSE:NIFTY'"))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync("INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, confirmed) SELECT id, '2026-01-01', 65, 'circular', true FROM market.instruments WHERE canonical_symbol = 'NSE:NIFTY'"))).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        await ExecuteAsync("INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed) SELECT id, '2026-01-01', 65, 'circular', 'https://example.invalid/circular', true FROM market.instruments WHERE canonical_symbol = 'NSE:NIFTY'");
    }
}
