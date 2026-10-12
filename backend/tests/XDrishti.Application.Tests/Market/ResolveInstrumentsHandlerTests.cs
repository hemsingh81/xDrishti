using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market;

public sealed class ResolveInstrumentsHandlerTests
{
    private readonly TrackingFixture _f = new();

    private static ResolveCandidate Share(string symbol, string? isin = null, bool retired = false, bool tracked = false) =>
        new(Guid.CreateVersion7(), symbol, symbol[4..], "share", isin, retired, tracked);

    private async Task<Result<ResolveResult>> ResolveAsync(params string?[] entries) =>
        await _f.Resolve.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput(entries!)), TrackingFixture.Ct);

    [Fact]
    public async Task Tickers_canonical_symbols_and_ISINs_match_ignoring_case_and_surrounding_space()
    {
        var reliance = Share("NSE:RELIANCE", "INE002A01018");
        var tcs = Share("NSE:TCS", "INE467B01029");
        var future = new ResolveCandidate(Guid.CreateVersion7(), "NSE:SBIN-2026-10-FUT", "SBIN", "future", null, false, false);
        _f.Data.ResolveRows = [reliance, tcs, future];

        var result = await ResolveAsync("  reliance ", "nse:tcs", "ine467b01029x", "NSE:sbin-2026-10-fut", "INE002A01018");

        var r = result.Value;
        r.Matched.Select(m => (m.Term, m.Symbol)).ShouldBe([("reliance", "NSE:RELIANCE"), ("nse:tcs", "NSE:TCS"), ("NSE:sbin-2026-10-fut", "NSE:SBIN-2026-10-FUT")]);
        r.Unmatched.ShouldBe([new UnmatchedEntry("ine467b01029x", "not_found")]);
        r.Duplicates.ShouldBe(1); // the ISIN names RELIANCE, already matched by its ticker
        r.Entries.ShouldBe(5);
    }

    [Fact]
    public async Task A_lookup_by_ISIN_works_when_nothing_else_names_the_entry()
    {
        var tcs = Share("NSE:TCS", "INE467B01029");
        _f.Data.ResolveRows = [tcs];

        var result = await ResolveAsync("ine467b01029");

        result.Value.Matched.Single().Id.ShouldBe(tcs.Id);
        _f.Data.LastResolve!.Value.Isins.ShouldBe(["INE467B01029"]);
        _f.Data.LastResolve!.Value.Symbols.ShouldContain("NSE:INE467B01029");
    }

    [Fact]
    public async Task The_store_is_asked_with_upper_case_keys_a_bare_ticker_gets_the_exchange_prefix()
    {
        await ResolveAsync("reliance", "nse:tcs", "NSE:Sbin-2026-10-FUT");

        _f.Data.LastResolve!.Value.Symbols.Order().ShouldBe(["NSE:RELIANCE", "NSE:SBIN-2026-10-FUT", "NSE:TCS"]);
        _f.Data.LastResolve!.Value.Isins.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeated_entries_in_any_case_are_collapsed_and_counted()
    {
        _f.Data.ResolveRows = [Share("NSE:TCS")];

        var result = await ResolveAsync("tcs", "TCS", " Tcs ", "NSE:TCS", "nope", "NOPE");

        var r = result.Value;
        r.Matched.Count.ShouldBe(1);
        r.Matched[0].Term.ShouldBe("tcs");
        r.Unmatched.Select(u => u.Term).ShouldBe(["nope"]);
        (r.Entries, r.Duplicates).ShouldBe((6, 3 + 1));
    }

    [Fact]
    public async Task Nothing_is_guessed_a_partial_name_or_a_near_miss_is_unmatched()
    {
        _f.Data.ResolveRows = [Share("NSE:RELIANCE"), Share("NSE:RELIANCEPP")];

        var result = await ResolveAsync("RELIAN", "Reliance Industries", "NSE:RELIANCE ");

        result.Value.Matched.Select(m => m.Symbol).ShouldBe(["NSE:RELIANCE"]);
        result.Value.Unmatched.Select(u => u.Term).ShouldBe(["RELIAN", "Reliance Industries"]);
        result.Value.Unmatched.ShouldAllBe(u => u.Reason == "not_found");
    }

    [Fact]
    public async Task An_ISIN_of_several_instruments_prefers_the_only_listed_one_otherwise_it_is_ambiguous()
    {
        var oldOne = Share("NSE:OLDNAME", "INE000A01011", retired: true);
        var newOne = Share("NSE:NEWNAME", "INE000A01011");
        var twinA = Share("NSE:TWINA", "INE000B01012");
        var twinB = Share("NSE:TWINB", "INE000B01012");
        _f.Data.ResolveRows = [oldOne, newOne, twinA, twinB];

        var result = await ResolveAsync("INE000A01011", "INE000B01012");

        result.Value.Matched.Select(m => m.Symbol).ShouldBe(["NSE:NEWNAME"]);
        result.Value.Unmatched.ShouldBe([new UnmatchedEntry("INE000B01012", "ambiguous")]);
    }

    [Fact]
    public async Task A_retired_and_tracked_flags_are_passed_on()
    {
        _f.Data.ResolveRows = [Share("NSE:OLD", retired: true), Share("NSE:LIVE", tracked: true)];

        var result = await ResolveAsync("old", "live");

        result.Value.Matched.Select(m => (m.Symbol, m.Retired, m.Tracked)).ShouldBe([("NSE:OLD", true, false), ("NSE:LIVE", false, true)]);
    }

    [Fact]
    public async Task Overlong_entries_and_ones_with_control_characters_are_unmatched_as_invalid_without_being_looked_up()
    {
        var result = await ResolveAsync(new string('x', 101), "TC\tS", "NSE:\0X", "ok");

        result.Value.Unmatched.Select(u => u.Reason).ShouldBe(["invalid", "invalid", "invalid", "not_found"]);
        _f.Data.LastResolve!.Value.Symbols.ShouldBe(["NSE:OK"]);
    }

    [Fact]
    public async Task Only_invalid_entries_do_not_reach_the_store()
    {
        await ResolveAsync("\u0001");

        _f.Data.LastResolve.ShouldBeNull();
    }

    [Fact]
    public async Task Blank_entries_are_ignored_and_an_empty_list_is_rejected()
    {
        var some = await ResolveAsync("", "  ", null, "x");
        some.Value.Entries.ShouldBe(1);

        foreach (var empty in new[] { await ResolveAsync(), await ResolveAsync(" ", ""), await _f.Resolve.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput(null)), TrackingFixture.Ct) })
        {
            empty.Error.Code.ShouldBe("instruments.invalid");
            empty.Error.Fields!.Single().Field.ShouldBe("symbols");
        }
    }

    [Fact]
    public async Task Five_thousand_entries_are_accepted_and_one_more_is_not()
    {
        var entries = Enumerable.Range(0, 5000).Select(i => $"S{i}").ToArray();

        (await ResolveAsync(entries)).IsSuccess.ShouldBeTrue();

        var tooMany = await ResolveAsync([.. entries, "S5000"]);
        tooMany.Error.Code.ShouldBe("instruments.invalid");
        tooMany.Error.Fields!.Single().Message.ShouldContain("5000");
    }

    [Fact]
    public async Task Repeats_count_towards_the_cap_so_a_huge_paste_of_one_symbol_is_refused()
    {
        var result = await ResolveAsync([.. Enumerable.Repeat("TCS", 5001)]);

        result.IsFailure.ShouldBeTrue();
    }
}
