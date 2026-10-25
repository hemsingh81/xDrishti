using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Application.Audit;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Tests.Audit;

public sealed class ListAuditHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ListAuditHandler Handler(IEnumerable<IAuditSource> sources) =>
        new(new AuditReader(sources, NullLogger<AuditReader>.Instance), new NoNames(), new FakeTimeProvider(Now), NullLogger<ListAuditHandler>.Instance);

    private static ListAuditQuery Query(int? limit = null, string? cursor = null, AuditFilters? filters = null) =>
        new(filters ?? new AuditFilters(null, null, null, null, null, null, null, null, null), limit, cursor, "owner@example.com");

    private static async Task<List<string>> AllAsync(ListAuditHandler handler, int limit, AuditFilters? filters = null)
    {
        var ids = new List<string>();
        string? cursor = null;
        for (var guard = 0; guard < 5000; guard++)
        {
            var page = await handler.HandleAsync(Query(limit, cursor, filters), Ct);
            page.IsSuccess.ShouldBeTrue();
            ids.AddRange(page.Value.Items.Select(i => i.Id));
            cursor = page.Value.NextCursor;
            if (cursor is null)
            {
                return ids;
            }
        }

        throw new InvalidOperationException("paging did not end");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 7)]
    [InlineData(6, 50)]
    [InlineData(6, 200)]
    [InlineData(2, 1)]
    public async Task Following_the_cursor_gives_every_row_exactly_once_in_order_even_with_ties(int sourceCount, int limit)
    {
        var random = new Random(sourceCount * 1000 + limit);
        var sources = Enumerable.Range(0, sourceCount).Select(i => new FakeSource((AuditSource)i, Enumerable.Range(0, 120).Select(_ => Now.AddMinutes(-random.Next(0, 40))))).ToList();
        var expected = sources.SelectMany(s => s.Rows).OrderByDescending(r => r.At).ThenByDescending(r => r.Source).ThenByDescending(r => r.Id, Comparer<string>.Create(FakeSource.CompareIds)).Select(r => AuditSources.Code(r.Source) + ":" + r.Id).ToList();

        var seen = await AllAsync(Handler(sources), limit);

        seen.Count.ShouldBe(expected.Count);
        seen.Distinct().Count().ShouldBe(seen.Count);
        seen.ShouldBe(expected);
    }

    [Fact]
    public async Task The_search_scans_in_chunks_and_the_cap_ends_a_page_without_skipping_or_repeating()
    {
        var quiet = new FakeSource(AuditSource.Account, Enumerable.Range(0, 12_000).Select(i => Now.AddSeconds(-i)), "nothing here");
        var loud = new FakeSource(AuditSource.Approval, Enumerable.Range(0, 30).Select(i => Now.AddSeconds(-i * 700)), "needle in text");
        var filters = new AuditFilters("2026-10-01", null, null, null, null, null, null, null, "needle");

        var seen = await AllAsync(Handler([quiet, loud]), 10, filters);

        seen.Count.ShouldBe(30);
        seen.Distinct().Count().ShouldBe(30);
        seen.ShouldAllBe(id => id.StartsWith("approval:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(7)]
    [InlineData(200)]
    public async Task Two_capped_stores_of_different_density_lose_and_repeat_nothing(int limit)
    {
        var dense = new FakeSource(AuditSource.Auth, Enumerable.Range(0, 12_000).Select(i => Now.AddSeconds(-i)), "nothing here");
        var sparse = new FakeSource(AuditSource.Account, Enumerable.Range(0, 12_000).Select(i => Now.AddSeconds(-i * 4)), "nothing here");
        var loud = new FakeSource(AuditSource.Approval, Enumerable.Range(0, 60).Select(i => Now.AddSeconds(-i * 700)), "needle in text");

        // Matches of the dense store lie beyond its own scan cap (row 5,000), between the horizons of the two capped stores.
        for (var i = 6000; i < 6010; i++)
        {
            dense.Rows[i] = dense.Rows[i] with { Text = "needle" };
        }

        var filters = new AuditFilters("2026-10-01", null, null, null, null, null, null, null, "needle");
        var expected = dense.Rows.Concat(loud.Rows).Where(r => r.Text!.Contains("needle", StringComparison.Ordinal)).OrderByDescending(r => r.At).ThenByDescending(r => r.Source).Select(r => AuditSources.Code(r.Source) + ":" + r.Id).ToList();

        var seen = await AllAsync(Handler([dense, sparse, loud]), limit, filters);

        seen.Count.ShouldBe(70);
        seen.Distinct().Count().ShouldBe(70);
        seen.ShouldBe(expected);
    }

    [Fact]
    public async Task The_capped_page_says_so()
    {
        var quiet = new FakeSource(AuditSource.Account, Enumerable.Range(0, 7_000).Select(i => Now.AddSeconds(-i)), "nothing here");

        var page = await Handler([quiet]).HandleAsync(Query(10, null, new AuditFilters(null, null, null, null, null, null, null, null, "needle")), Ct);

        page.Value.Items.ShouldBeEmpty();
        page.Value.NextCursor.ShouldNotBeNull();
        page.Value.Limits.ShouldContain(l => l.Contains("narrow the date range", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_store_is_named_and_the_others_are_still_listed()
    {
        var good = new FakeSource(AuditSource.Plan, [Now.AddMinutes(-1)]);
        var bad = new FakeSource(AuditSource.Auth, []) { Fail = true };

        var page = await Handler([good, bad]).HandleAsync(Query(), Ct);

        page.Value.Items.Count.ShouldBe(1);
        page.Value.Sources.Single(s => s.Source == "auth").ShouldBe(new AuditSourceState("auth", "unavailable", "read_failed"));
        page.Value.Sources.Single(s => s.Source == "plan").State.ShouldBe("value");
        page.Value.Sources.Single(s => s.Source == "learning").State.ShouldBe("empty");
        page.Value.Limits.ShouldContain(l => l.StartsWith("Only what is recorded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_search_cannot_reveal_what_the_redactor_hides()
    {
        var source = new FakeSource(AuditSource.Account, [Now.AddMinutes(-1)], "set password=hunter2 for the account");

        foreach (var (q, found) in new[] { ("hunter", 0), ("password=hun", 0), ("redacted", 1), ("account", 1) })
        {
            var page = await Handler([source]).HandleAsync(Query(filters: new AuditFilters(null, null, null, null, null, null, null, null, q)), Ct);

            page.Value.Items.Count.ShouldBe(found, q);
        }

        var all = await Handler([source]).HandleAsync(Query(), Ct);
        all.Value.Items.Single().Summary.ShouldNotContain("hunter2");
        all.Value.Items.Single().Redacted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_filter_narrows_and_an_unknown_value_is_refused_naming_the_field()
    {
        var account = new FakeSource(AuditSource.Account, [Now.AddMinutes(-1)], kind: "PriceAlertCreated");
        var auth = new FakeSource(AuditSource.Auth, [Now.AddMinutes(-2)], kind: "LoginFailed");
        var handler = Handler([account, auth]);

        var byCategory = await handler.HandleAsync(Query(filters: new AuditFilters(null, null, null, "alert", null, null, null, null, null)), Ct);
        var byOutcome = await handler.HandleAsync(Query(filters: new AuditFilters(null, null, null, null, null, "failed", null, null, null)), Ct);
        var both = await handler.HandleAsync(Query(filters: new AuditFilters(null, null, null, "alert", null, "failed", null, null, null)), Ct);

        byCategory.Value.Items.Select(i => i.Id.Split(':')[0]).ShouldBe(["account"]);
        byOutcome.Value.Items.Select(i => i.Id.Split(':')[0]).ShouldBe(["auth"]);
        both.Value.Items.ShouldBeEmpty();
        foreach (var (name, filters) in new[]
        {
            ("source", new AuditFilters(null, null, "nope", null, null, null, null, null, null)),
            ("outcome", new AuditFilters(null, null, null, null, null, "bad", null, null, null)),
            ("actor", new AuditFilters(null, null, null, null, null, null, "bad", null, null)),
            ("kind", new AuditFilters(null, null, null, null, "bad", null, null, null, null)),
            ("account", new AuditFilters(null, null, null, null, null, null, null, "bad", null)),
            ("q", new AuditFilters(null, null, null, null, null, null, null, null, "ab")),
            ("from", new AuditFilters("2025-01-01", "2026-10-12", null, null, null, null, null, null, null)),
            ("to", new AuditFilters(null, "12/10/2026", null, null, null, null, null, null, null)),
        })
        {
            var refused = await handler.HandleAsync(Query(filters: filters), Ct);

            refused.IsFailure.ShouldBeTrue(name);
            refused.Error.Code.ShouldBe(AuditErrors.InvalidCode);
            refused.Error.Fields!.Select(f => f.Field).ShouldContain(name);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task A_limit_out_of_range_is_refused(int limit) =>
        (await Handler([]).HandleAsync(Query(limit), Ct)).Error.Fields!.Single().Field.ShouldBe("limit");

    [Fact]
    public async Task A_foreign_cursor_is_refused()
    {
        var refused = await Handler([]).HandleAsync(Query(cursor: "bm90LWEtY3Vyc29y"), Ct);

        refused.Error.Fields!.Single().Field.ShouldBe("cursor");
    }

    [Fact]
    public async Task A_sign_in_with_another_address_shows_no_address()
    {
        var auth = new FakeSource(AuditSource.Auth, [Now.AddMinutes(-1)], "wrong", "LoginFailed", actor: "someone-else@example.com");

        var page = await Handler([auth]).HandleAsync(Query(), Ct);

        page.Value.Items.Single().ActorKind.ShouldBe("other_address");
        page.Value.Items.Single().Actor.ShouldBeNull();
    }

    private sealed class NoNames : IAuditAccountNames
    {
        public ValueTask<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    /// <summary>An in-memory store with the same keyset semantics as the SQL readers.</summary>
    private sealed class FakeSource : IAuditSource
    {
        private static long _next;

        public FakeSource(AuditSource source, IEnumerable<DateTimeOffset> times, string? text = null, string kind = "AccountCreated", string? actor = "owner@example.com")
        {
            Source = source;
            Rows =
            [
                .. times.Select(at =>
                {
                    var n = Interlocked.Increment(ref _next);
                    var id = AuditSources.IsUuid(source) ? new Guid((int)n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]).ToString("D") : n.ToString(CultureInfo.InvariantCulture);
                    return new AuditRaw(source, id, at, kind, actor, null, text, null, null, []);
                }),
            ];
        }

        public bool Fail { get; init; }

        public List<AuditRaw> Rows { get; }

        public AuditSource Source { get; }

        public static int CompareIds(string? a, string? b) => a!.Length == b!.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length);

        public ValueTask<IReadOnlyList<AuditRaw>> ReadAsync(AuditRead read, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("down");
            }

            var rows = Rows.Where(r => r.At >= read.From && r.At < read.To)
                .Where(r => read.After is not { } a || (!a.SameAt ? r.At < a.At : a.BeforeId is null ? r.At <= a.At : r.At < a.At || (r.At == a.At && CompareIds(r.Id, a.BeforeId) < 0)))
                .Where(r => read.Kinds is null || read.Kinds.Contains(r.Kind))
                .OrderByDescending(r => r.At).ThenByDescending(r => r.Id, Comparer<string>.Create(CompareIds))
                .Take(read.Take).ToList();
            return ValueTask.FromResult<IReadOnlyList<AuditRaw>>(rows);
        }

        public ValueTask<AuditRaw?> GetAsync(string id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public ValueTask<IReadOnlyList<AuditKindCount>> CountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<AuditKindCount>>([.. Rows.GroupBy(r => r.Kind).Select(g => new AuditKindCount(g.Key, g.Count()))]);
    }
}
