#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using System.Text;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Market.Lists;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Tests.Market.Lists;

/// <summary>DATA-01 (ADR 0274) criteria 1 to 4 and 8 at the use-case level, over fake ports: no request while off, refusal by the allow-list, fail-closed parsing, empty day stored, insert-only, cap.</summary>
public sealed class ListsBackfillTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig
    {
        public IListHistorySettings Settings { get; } = Substitute.For<IListHistorySettings>();
        public IListArchiveSource Source { get; } = Substitute.For<IListArchiveSource>();
        public IListDayStore Store { get; } = Substitute.For<IListDayStore>();
        public IBasketSourceStore Instruments { get; } = Substitute.For<IBasketSourceStore>();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));

        public Rig(bool enabled = true, int max = 100)
        {
            Settings.Enabled.Returns(enabled);
            Settings.MaxPerDay.Returns(max);
            Settings.Delay.Returns(TimeSpan.Zero);
            Store.CoveredAsync(default, default, default, default).ReturnsForAnyArgs(ValueTask.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly>()));
            Store.InsertAsync(default, default, default!, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(true));
            Instruments.FindInstrumentsAsync(default!, default!, default).ReturnsForAnyArgs(call => ValueTask.FromResult(Match(call.ArgAt<IReadOnlyCollection<string>>(1))));
        }

        public Func<IReadOnlyCollection<string>, IReadOnlyList<MasterInstrument>> Match { get; set; } = symbols => [.. symbols.Select(s => new MasterInstrument(Guid.NewGuid(), s, null, null))];

        public ListsBackfillHandler Handler => new(Settings, Source, Store, Instruments, Clock);

        public void File(DateOnly date, string text) => Source.FetchBanAsync(date, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ListArchiveFetch.Ok(Encoding.UTF8.GetBytes(text))));
    }

    private static string Ban(DateOnly date, params string[] symbols) =>
        $"Securities in Ban For Trade Date {date:dd-MMM-yyyy}:\r\n" + string.Concat(symbols.Select((s, i) => $"{i + 1},{s}\r\n"));

    [Fact]
    public async Task With_the_switch_off_it_refuses_and_sends_no_request()
    {
        var rig = new Rig(enabled: false);

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday), Ct);

        result.Error.Code.ShouldBe("history.disabled");
        await rig.Source.DidNotReceiveWithAnyArgs().FetchBanAsync(default, default);
    }

    [Fact]
    public async Task An_endpoint_that_is_not_on_the_allow_list_refuses_with_history_not_allowed_and_stores_nothing()
    {
        var rig = new Rig();
        rig.Source.FetchBanAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ListArchiveFetch.NotAllowed("refused")));

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday.AddDays(4)), Ct);

        result.Error.Code.ShouldBe("history.not_allowed");
        await rig.Source.ReceivedWithAnyArgs(1).FetchBanAsync(default, default);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default, default, default!, default!, default);
    }

    [Theory]
    [InlineData("<html>not found</html>")]
    [InlineData("Securities in Ban For Trade Date 31-FOO-2026:\r\n1,AAA\r\n")]
    [InlineData("Securities in Ban For Trade Date 02-NOV-2026:\r\n1,AAA\r\nbad row here !\r\n")]
    [InlineData("Securities in Ban For Trade Date 03-NOV-2026:\r\n1,AAA\r\n")]
    public async Task A_bad_file_or_one_for_another_date_is_rejected_and_stores_nothing(string text)
    {
        var rig = new Rig();
        rig.File(Monday, text);

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday), Ct);

        result.Value.Rejected.ShouldBe(1);
        result.Value.Stored.ShouldBe(0);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_valid_empty_file_stores_an_empty_covered_day()
    {
        var rig = new Rig();
        rig.File(Monday, Ban(Monday));

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday), Ct);

        result.Value.Stored.ShouldBe(1);
        await rig.Store.Received(1).InsertAsync(ListBaskets.Ban.Id, Monday, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), "archive", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_banned_symbol_the_catalogue_cannot_match_rejects_the_day_rather_than_reading_as_not_banned()
    {
        var rig = new Rig();
        rig.File(Monday, Ban(Monday, "AAA", "ZZZ"));
        rig.Match = _ => [new MasterInstrument(Guid.NewGuid(), "AAA", null, null)];

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday), Ct);

        result.Value.Rejected.ShouldBe(1);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_day_already_stored_is_not_requested_again_and_a_racing_second_insert_is_counted_as_a_no_op()
    {
        var rig = new Rig();
        rig.Store.CoveredAsync(default, default, default, default).ReturnsForAnyArgs(ValueTask.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly> { Monday }));
        rig.File(Monday.AddDays(1), Ban(Monday.AddDays(1)));
        rig.Store.InsertAsync(default, default, default!, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(false));

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday.AddDays(1)), Ct);

        result.Value.Requested.ShouldBe(1);
        result.Value.AlreadyStored.ShouldBe(2);
        result.Value.Stored.ShouldBe(0);
    }

    [Fact]
    public async Task A_run_stops_at_the_per_run_cap_and_skips_weekends()
    {
        var rig = new Rig(max: 6);
        rig.Source.FetchBanAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ListArchiveFetch.Absent()));

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday.AddDays(8)), Ct);

        result.Value.Requested.ShouldBe(6);
        result.Value.Capped.ShouldBeTrue();
        await rig.Source.DidNotReceive().FetchBanAsync(new DateOnly(2026, 11, 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dry_run_fetches_and_parses_but_stores_nothing()
    {
        var rig = new Rig();
        rig.File(Monday, Ban(Monday, "AAA"));

        var result = await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, Monday, DryRun: true), Ct);

        result.Value.Requested.ShouldBe(1);
        result.Value.Stored.ShouldBe(0);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task A_range_ending_today_or_reversed_is_refused()
    {
        var rig = new Rig();

        (await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday, new DateOnly(2026, 11, 12)), Ct)).Error.Code.ShouldBe("history.bad_range");
        (await rig.Handler.HandleAsync(new ListsBackfillCommand(Monday.AddDays(3), Monday), Ct)).Error.Code.ShouldBe("history.bad_range");
    }

    [Fact]
    public async Task The_probe_stores_nothing_asks_at_most_five_files_and_is_refused_while_off()
    {
        var rig = new Rig();
        rig.File(Monday, Ban(Monday, "AAA"));
        var probe = new ListsProbeHandler(rig.Settings, rig.Source, rig.Clock);

        var report = await probe.HandleAsync(new ListsProbeCommand([Monday]), Ct);
        var many = await probe.HandleAsync(new ListsProbeCommand([.. Enumerable.Range(0, 6).Select(i => Monday.AddDays(i))]), Ct);
        var off = new Rig(enabled: false);
        var refused = await new ListsProbeHandler(off.Settings, off.Source, off.Clock).HandleAsync(new ListsProbeCommand([Monday]), Ct);

        report.Value.Lines[0].ShouldContain("1 symbols");
        many.Error.Code.ShouldBe("history.bad_range");
        refused.Error.Code.ShouldBe("history.disabled");
        await off.Source.DidNotReceiveWithAnyArgs().FetchBanAsync(default, default);
        await rig.Store.DidNotReceiveWithAnyArgs().InsertAsync(default, default, default!, default!, default);
    }
}
