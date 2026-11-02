using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-02 on a real TimescaleDB (criteria 2 to 7): the envelope constraints and the supersede trigger, the idempotent and parallel insert, revisions, <c>AsOf</c> from stored rows equal to the domain rule whatever the insert order, and the whole ingest from a real
/// inbox folder into a real store folder.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class FilingsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private Guid _reliance;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _reliance = await SeedAsync("RELIANCE");
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<FilingInsertResult> InsertAsync(NewFiling filing) => WithAsync(p => p.GetRequiredService<IFilingStore>().InsertAsync(filing, Ct).AsTask());

    private Task<AsOfResult> AsOfAsync(Guid instrument, DateTimeOffset t, FilingKind kind = FilingKind.Results) => WithAsync(p => p.GetRequiredService<IPointInTimeFilings>().AsOf(instrument, kind, t, Ct).AsTask());

    private async Task<string?> SqlStateAsync(string sql, params (string, object)[] parameters)
    {
        await using var connection = await OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private const string RawInsert = "INSERT INTO filings.filings (id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, source, content_sha256, file_bytes, file_name, supersedes) VALUES ";

    [Fact]
    public async Task An_envelope_is_stored_with_its_deterministic_id_and_read_back_by_its_hash()
    {
        var filing = FilingTestKit.Ok(_reliance, "one", FilingTestKit.Q1, FilingTestKit.Broadcast);

        var result = await InsertAsync(filing);
        var stored = await WithAsync(p => p.GetRequiredService<IFilingStore>().FindByHashAsync(filing.ContentSha256, Ct).AsTask());

        result.Outcome.ShouldBe(FilingInsertOutcome.Stored);
        stored.ShouldNotBeNull().Id.ShouldBe(filing.Id);
        (stored.BroadcastTs, stored.State, stored.PeriodEnd, stored.Kind, stored.Supersedes).ShouldBe((FilingTestKit.Broadcast, BroadcastState.Ok, FilingTestKit.Q1, FilingKind.Results, null));
    }

    [Fact]
    public async Task The_same_file_twice_is_one_row_and_the_same_bytes_with_another_sidecar_are_a_conflict_that_changes_nothing()
    {
        var filing = FilingTestKit.Ok(_reliance, "one", FilingTestKit.Q1, FilingTestKit.Broadcast);
        await InsertAsync(filing);

        var again = await InsertAsync(filing with { FileName = "other.xml" });
        var conflict = await InsertAsync(filing with { BroadcastTs = FilingTestKit.Broadcast.AddHours(1) });
        var otherPeriod = await InsertAsync(filing with { PeriodEnd = new DateOnly(2026, 3, 31) });

        again.Outcome.ShouldBe(FilingInsertOutcome.Duplicate);
        conflict.Outcome.ShouldBe(FilingInsertOutcome.SidecarConflict);
        conflict.Existing.ShouldNotBeNull().BroadcastTs.ShouldBe(FilingTestKit.Broadcast);
        otherPeriod.Outcome.ShouldBe(FilingInsertOutcome.SidecarConflict);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(1);
    }

    [Fact]
    public async Task Eight_parallel_inserts_of_the_same_file_store_it_once()
    {
        var filing = FilingTestKit.Ok(_reliance, "parallel", FilingTestKit.Q1, FilingTestKit.Broadcast);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => InsertAsync(filing), Ct)));

        results.Count(r => r.Outcome == FilingInsertOutcome.Stored).ShouldBe(1);
        results.Count(r => r.Outcome == FilingInsertOutcome.Duplicate).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_revisions_of_one_period_form_one_linear_chain_and_every_file_is_stored()
    {
        await InsertAsync(FilingTestKit.Ok(_reliance, "v1", FilingTestKit.Q1, FilingTestKit.Broadcast));
        var files = Enumerable.Range(2, 6).Select(i => FilingTestKit.Ok(_reliance, "v" + i, FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(i))).ToList();

        var results = await Task.WhenAll(files.Select(f => Task.Run(() => InsertAsync(f), Ct)));

        results.ShouldAllBe(r => r.Outcome == FilingInsertOutcome.Stored || r.Outcome == FilingInsertOutcome.StoredOutOfOrder);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT supersedes FROM filings.filings WHERE supersedes IS NOT NULL GROUP BY supersedes HAVING count(*) > 1) x")).ShouldBe(0);
        var latest = await AsOfAsync(_reliance, FilingTestKit.Broadcast.AddYears(1));
        latest.Filings.ShouldHaveSingleItem().BroadcastTs.ShouldBe(FilingTestKit.Broadcast.AddDays(7));
    }

    [Fact]
    public async Task A_later_file_of_the_period_supersedes_an_earlier_one_is_stored_out_of_order_and_an_equal_time_is_not_stored()
    {
        var original = FilingTestKit.Ok(_reliance, "orig", FilingTestKit.Q1, FilingTestKit.Broadcast);
        await InsertAsync(original);

        var revision = await InsertAsync(FilingTestKit.Ok(_reliance, "restated", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(37)));
        var early = await InsertAsync(FilingTestKit.Ok(_reliance, "early", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(-3)));
        var tie = await InsertAsync(FilingTestKit.Ok(_reliance, "tie", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(37)));

        (revision.Outcome, revision.Supersedes).ShouldBe((FilingInsertOutcome.Stored, original.Id));
        (early.Outcome, early.Supersedes).ShouldBe((FilingInsertOutcome.StoredOutOfOrder, null));
        tie.Outcome.ShouldBe(FilingInsertOutcome.AmbiguousOrder);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(3);
    }

    [Fact]
    public async Task A_file_without_a_time_is_stored_unavailable_and_is_never_listed_by_asOf()
    {
        await InsertAsync(FilingTestKit.Ok(_reliance, "undated", FilingTestKit.Q1, null));

        var answer = await AsOfAsync(_reliance, FilingTestKit.Broadcast.AddYears(5));

        (answer.State, answer.Filings.Count, answer.UnavailableCount).ShouldBe((AsOfResult.Unavailable, 0, 1));
        (await ScalarAsync<string>("SELECT unavailable_reason FROM filings.filings")).ShouldBe("date_only");
    }

    [Fact]
    public async Task The_hand_example_holds_in_the_database_at_16_16_59_and_16_17_00_and_for_the_restatement()
    {
        var ist = TimeSpan.FromMinutes(330);
        await InsertAsync(FilingTestKit.Ok(_reliance, "orig", FilingTestKit.Q1, FilingTestKit.Broadcast));
        await InsertAsync(FilingTestKit.Ok(_reliance, "restated", FilingTestKit.Q1, new DateTimeOffset(2026, 8, 30, 10, 0, 0, ist)));

        (await AsOfAsync(_reliance, new DateTimeOffset(2026, 7, 24, 16, 16, 59, ist))).State.ShouldBe(AsOfResult.NoneVisible);
        var at = await AsOfAsync(_reliance, new DateTimeOffset(2026, 7, 24, 16, 17, 0, ist));
        var before = await AsOfAsync(_reliance, new DateTimeOffset(2026, 8, 29, 12, 0, 0, ist));
        var after = await AsOfAsync(_reliance, new DateTimeOffset(2026, 8, 30, 10, 5, 0, ist));

        at.Filings.ShouldHaveSingleItem().FileBytes.ShouldBe(4);
        before.Filings.ShouldHaveSingleItem().FileBytes.ShouldBe(4);
        after.Filings.ShouldHaveSingleItem().FileBytes.ShouldBe(8);
    }

    [Fact]
    public async Task AsOf_from_stored_rows_equals_the_domain_rule_over_all_rows_whatever_the_insert_order()
    {
        var other = await SeedAsync("TCS");
        var files = new List<(string Content, DateOnly? Period, DateTimeOffset? Ts)>
        {
            ("a1", new DateOnly(2026, 3, 31), FilingTestKit.Broadcast.AddDays(-90)),
            ("a2", new DateOnly(2026, 6, 30), FilingTestKit.Broadcast),
            ("a3", new DateOnly(2026, 6, 30), FilingTestKit.Broadcast.AddDays(37)),
            ("a4", new DateOnly(2026, 6, 30), FilingTestKit.Broadcast.AddDays(-2)),
            ("a5", new DateOnly(2026, 9, 30), null),
            ("a6", new DateOnly(2026, 9, 30), FilingTestKit.Broadcast.AddDays(100)),
            ("a7", null, FilingTestKit.Broadcast.AddDays(10)),
        };
        foreach (var file in files)
        {
            await InsertAsync(FilingTestKit.Ok(_reliance, file.Content, file.Period, file.Ts));
        }

        foreach (var file in files.AsEnumerable().Reverse())
        {
            await InsertAsync(FilingTestKit.Ok(other, file.Content + "-other", file.Period, file.Ts));
        }

        var stored = new List<StoredFiling>();
        foreach (var file in files)
        {
            stored.Add((await WithAsync(p => p.GetRequiredService<IFilingStore>().FindByHashAsync(FilingTestKit.Sha(file.Content), Ct).AsTask()))!);
        }

        var sources = Factory.Services;
        var random = new Random(7);
        var instants = new List<DateTimeOffset>();
        for (var i = 0; i < 60; i++)
        {
            instants.Add(FilingTestKit.Broadcast.AddDays(-100).AddMinutes(random.Next(0, 300 * 24 * 60)));
        }

        foreach (var row in stored.Where(r => r.BroadcastTs is not null))
        {
            instants.Add(row.BroadcastTs!.Value + TimeSpan.FromMinutes(5)); // exactly on broadcast + lag
            instants.Add(row.BroadcastTs!.Value + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        }

        var seen = 0;
        foreach (var lagMinutes in new[] { 5, 0 })
        {
            var reader = new PointInTimeFilings(sources.GetRequiredService<NpgsqlDataSource>(), new LagSettings(lagMinutes));
            foreach (var t in instants)
            {
                var viaDb = await reader.AsOf(_reliance, FilingKind.Results, t, Ct);
                var viaRule = PointInTimeRule.AsOf(stored, t, TimeSpan.FromMinutes(lagMinutes));

                FilingSamples(viaDb).ShouldBe(FilingSamples(viaRule), $"lag {lagMinutes} t {t:O}");
                seen += viaDb.Filings.Count > 0 ? 1 : 0;
                if (lagMinutes == 5)
                {
                    var viaOther = await AsOfAsync(other, t);
                    viaDb.Filings.Select(f => (f.PeriodEnd, f.BroadcastTs)).ShouldBe(viaOther.Filings.Select(f => (f.PeriodEnd, f.BroadcastTs)));
                    (viaDb.State, viaDb.UnavailableCount).ShouldBe((viaOther.State, viaOther.UnavailableCount));
                }
            }
        }

        seen.ShouldBeGreaterThan(40);
    }

    private static string FilingSamples(AsOfResult r) => $"{r.State}|{r.UnavailableCount}|" + string.Join(";", r.Filings.Select(f => $"{f.Id}/{f.PeriodEnd:yyyy-MM-dd}/{f.BroadcastTs?.UtcDateTime:O}/{f.Supersedes}"));

    private sealed class LagSettings(int lag) : IFilingSettings
    {
        public int LagMinutes => lag;

        public long MaxFilesBytes => long.MaxValue;

        public long MinFreeBytes => 0;
    }

    [Fact]
    public async Task A_sidecar_time_with_seven_fraction_digits_is_a_duplicate_on_the_next_run_and_a_revision_under_a_microsecond_later_is_not_a_trigger_failure()
    {
        var seven = FilingTestKit.Broadcast.AddTicks(1234567);
        NewFiling Build(string content, DateTimeOffset ts) => FilingTestKit.Ok(_reliance, content, FilingTestKit.Q1, NewFiling.ToMicroseconds(ts));

        var first = await InsertAsync(Build("seven", seven));
        var again = await InsertAsync(Build("seven", seven));
        var nearly = await InsertAsync(Build("seven-b", seven.AddTicks(1)));
        var later = await InsertAsync(Build("seven-c", seven.AddTicks(10)));

        (first.Outcome, again.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Duplicate));
        nearly.Outcome.ShouldBe(FilingInsertOutcome.AmbiguousOrder);
        later.Outcome.ShouldBe(FilingInsertOutcome.Stored);
    }

    [Fact]
    public async Task A_time_equal_to_an_older_stored_time_of_the_group_is_ambiguous()
    {
        await InsertAsync(FilingTestKit.Ok(_reliance, "a", FilingTestKit.Q1, FilingTestKit.Broadcast));
        await InsertAsync(FilingTestKit.Ok(_reliance, "b", FilingTestKit.Q1, FilingTestKit.Broadcast.AddHours(1)));

        var c = await InsertAsync(FilingTestKit.Ok(_reliance, "c", FilingTestKit.Q1, FilingTestKit.Broadcast));

        c.Outcome.ShouldBe(FilingInsertOutcome.AmbiguousOrder);
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_hash_a_state_that_disagrees_with_the_time_a_self_supersede_and_a_word_outside_the_lists()
    {
        var id = Guid.NewGuid();
        var sha = new string('a', 64);
        string Row(Guid rowId, string hash, string state, string? ts, string? reason, string kind = "results", string? supersedes = null, string? period = "'2026-06-30'") =>
            $"({Q(rowId)}, {Q(_reliance)}, '{kind}', {period ?? "NULL"}, {(ts is null ? "NULL" : $"'{ts}'")}, '{state}', {(reason is null ? "NULL" : $"'{reason}'")}, now(), 'folder_drop', '{hash}', 1, 'f.xml', {(supersedes is null ? "NULL" : Q(Guid.Parse(supersedes)))})";

        (await SqlStateAsync(RawInsert + Row(id, sha, "ok", "2026-07-24T10:42:00Z", null))).ShouldBeNull();
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), sha, "ok", "2026-07-25T10:42:00Z", null))).ShouldBe("23505"); // the hash is unique
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), new string('b', 64), "ok", null, null))).ShouldBe("23514"); // ok without a time
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), new string('c', 64), "unavailable", "2026-07-24T10:42:00Z", "date_only"))).ShouldBe("23514"); // a time with state unavailable
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), new string('d', 64), "unavailable", null, null))).ShouldBe("23514"); // no reason
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), new string('e', 64), "unavailable", null, "because"))).ShouldBe("23514");
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), new string('f', 64), "ok", "2026-07-24T10:42:00Z", null, kind: "memo"))).ShouldBe("23514");
        (await SqlStateAsync(RawInsert + Row(Guid.NewGuid(), "not-a-hash", "ok", "2026-07-24T10:42:00Z", null))).ShouldBe("23514");
        var selfId = Guid.NewGuid();
        (await SqlStateAsync(RawInsert + Row(selfId, new string('1', 64), "ok", "2026-07-26T10:42:00Z", null, supersedes: selfId.ToString()))).ShouldBe("23514");
    }

    [Fact]
    public async Task The_database_refuses_a_supersede_that_is_not_a_later_revision_of_the_same_period_and_a_second_revision_of_one_filing()
    {
        var first = FilingTestKit.Ok(_reliance, "first", FilingTestKit.Q1, FilingTestKit.Broadcast);
        await InsertAsync(first);
        var tcs = await SeedAsync("TCS");
        string Insert(NewFiling f, Guid supersedes) => RawInsert + $"({Q(f.Id)}, {Q(f.InstrumentId)}, 'results', '{f.PeriodEnd:yyyy-MM-dd}', '{f.BroadcastTs!.Value.UtcDateTime:O}', 'ok', NULL, now(), 'folder_drop', '{f.ContentSha256}', 1, 'f.xml', {Q(supersedes)})";

        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "earlier", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(-1)), first.Id))).ShouldBe("23514");
        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "equal", FilingTestKit.Q1, FilingTestKit.Broadcast), first.Id))).ShouldBe("23514");
        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "period", new DateOnly(2026, 3, 31), FilingTestKit.Broadcast.AddDays(1)), first.Id))).ShouldBe("23514");
        (await SqlStateAsync(Insert(FilingTestKit.Ok(tcs, "instrument", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(1)), first.Id))).ShouldBe("23514");
        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "ghost", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(1)), Guid.NewGuid()))).ShouldBe("23514"); // a revision of a filing that does not exist
        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "good", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(1)), first.Id))).ShouldBeNull();
        (await SqlStateAsync(Insert(FilingTestKit.Ok(_reliance, "second", FilingTestKit.Q1, FilingTestKit.Broadcast.AddDays(2)), first.Id))).ShouldBe("23505");
    }

    [Fact]
    public async Task The_four_fact_tables_are_created_empty_and_the_ingest_writes_into_none_of_them()
    {
        await InsertAsync(FilingTestKit.Ok(_reliance, "one", FilingTestKit.Q1, FilingTestKit.Broadcast));

        foreach (var table in new[] { "result_facts", "shareholding", "announcements", "calendar" })
        {
            (await ScalarAsync<long>($"SELECT count(*) FROM filings.{table}")).ShouldBe(0, table);
        }
    }

    [Fact]
    public async Task A_whole_inbox_goes_through_the_real_source_and_the_real_store_folder_once_and_a_rerun_changes_nothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "xd-filings-it-" + Guid.NewGuid().ToString("N"));
        var inbox = Path.Combine(root, "inbox");
        Directory.CreateDirectory(inbox);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            Drop(inbox, "r1.xml", "<results v='1'/>", "RELIANCE", "results", "2026-06-30", "2026-07-24T16:12:00+05:30");
            Drop(inbox, "r2.xml", "<results v='2'/>", "RELIANCE", "results", "2026-06-30", "2026-08-30T10:00:00+05:30");
            Drop(inbox, "u.xml", "<results v='3'/>", "RELIANCE", "results", "2026-03-31", "2026-04-20");
            Drop(inbox, "x.xml", "<results v='4'/>", "NOSUCH", "results", "2026-06-30", "2026-07-24T16:12:00+05:30");
            var options = Options.Create(new FilingsOptions { InboxPath = inbox, FilesPath = Path.Combine(root, "files") });
            using var scope = Factory.Services.CreateScope();
            var handler = new IngestFilingsHandler(
                new FolderDropFilingsSource(options, TimeProvider.System, NullLogger<FolderDropFilingsSource>.Instance),
                scope.ServiceProvider.GetRequiredService<IFilingInstruments>(),
                scope.ServiceProvider.GetRequiredService<IFilingStore>(),
                new FilingFiles(options),
                scope.ServiceProvider.GetRequiredService<IFilingSettings>(),
                TimeProvider.System,
                NullLogger<IngestFilingsHandler>.Instance);

            var first = (await handler.HandleAsync(new IngestFilingsCommand(true), Ct)).Value;
            var files = Directory.GetFiles(Path.Combine(root, "files"), "*", SearchOption.AllDirectories);
            var second = (await handler.HandleAsync(new IngestFilingsCommand(true), Ct)).Value;

            (first.Stored, first.Revisions, first.Unavailable).ShouldBe((3, 1, 1));
            first.Rejected.ShouldBe(new Dictionary<string, int> { ["unknown_instrument"] = 1 });
            files.Length.ShouldBe(3);
            (second.Stored, second.Duplicate).ShouldBe((0, 3));
            Directory.GetFiles(Path.Combine(root, "files"), "*", SearchOption.AllDirectories).Length.ShouldBe(3);
            Directory.GetFiles(inbox).Length.ShouldBe(8); // the inbox is untouched: four files and four sidecars
            (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(3);
            var answer = await AsOfAsync(_reliance, new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
            answer.Filings.Single(f => f.PeriodEnd == FilingTestKit.Q1).BroadcastTs.ShouldBe(new DateTimeOffset(2026, 8, 30, 4, 30, 0, TimeSpan.Zero));
            (answer.UnavailableCount, answer.Filings.Count).ShouldBe((1, 1));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Drop(string inbox, string name, string content, string symbol, string kind, string period, string broadcast)
    {
        File.WriteAllText(Path.Combine(inbox, name), content);
        File.WriteAllText(Path.Combine(inbox, name + ".meta.json"), $$"""{"schema":1,"symbol":"{{symbol}}","kind":"{{kind}}","period_end":"{{period}}","broadcast_ts":"{{broadcast}}"}""");
    }

    private static string Q(Guid id) => $"'{id.ToString("D", CultureInfo.InvariantCulture)}'";
}
