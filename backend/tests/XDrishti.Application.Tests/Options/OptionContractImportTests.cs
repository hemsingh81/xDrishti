using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Options;
using XDrishti.Application.Options.Import;
using XDrishti.Domain.Options;

namespace XDrishti.Application.Tests.Options;

/// <summary>Spec P14-01 criteria 1, 2, 4 and 8: the listing, the default fixed once, unknown and unmapped underlyings, and that the step never fails a refresh.</summary>
public sealed class OptionContractImportTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-000000000013");

    private readonly InMemoryOptionData _data = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OptionContractImport Import => new(_data, _data, NullLogger<OptionContractImport>.Instance);

    private static IEnumerable<string> Shares(int count) => Enumerable.Range(1, count).Select(i => $"NSE:S{i:00}");

    private static OptionContractRow Row(long id, string underlying, string expiry = "2026-11-09", int lot = 65) =>
        new(id, underlying, "OPTIDX", DateOnly.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture), 2_500_000, "CE", "week", lot, 5);

    [Fact]
    public async Task Without_a_setting_the_default_is_the_four_indices_and_the_30_most_traded_shares_written_once_as_the_system()
    {
        _data.Setting = null;
        _data.MostTraded.AddRange(Shares(31));

        var (listed, notes) = await Import.ListingAsync(Today, Now, Ct);

        listed.Symbols.Count.ShouldBe(34);
        listed.Symbols.ShouldContain("NSE:MIDCPNIFTY");
        listed.Symbols.ShouldNotContain("NSE:S31");
        _data.Setting.ShouldBe(listed.ToStored());
        _data.SettingActor.ShouldBe(OptionContractImport.FirstImportActor);
        notes.ShouldBeEmpty("fixing the default is logged, not a warning");
    }

    [Fact]
    public async Task A_ranking_on_stale_daily_bars_is_used_for_the_run_but_not_written_and_reported()
    {
        _data.Setting = null;
        _data.MostTraded.AddRange(Shares(30));
        _data.NewestBar = Today.AddDays(-(OptionContractImport.MaxBarAgeDays + 1));

        var (listed, notes) = await Import.ListingAsync(Today, Now, Ct);

        listed.Symbols.Count.ShouldBe(34);
        _data.Setting.ShouldBeNull();
        notes.ShouldHaveSingleItem().ShouldContain("2026-10-24");
    }

    [Fact]
    public async Task A_ranking_whose_newest_bar_is_seven_days_old_is_still_fresh()
    {
        _data.Setting = null;
        _data.MostTraded.AddRange(Shares(30));
        _data.NewestBar = Today.AddDays(-OptionContractImport.MaxBarAgeDays);

        (await Import.ListingAsync(Today, Now, Ct)).Problems.ShouldBeEmpty();

        _data.Setting.ShouldNotBeNull();
    }

    [Fact]
    public async Task With_fewer_than_30_ranked_shares_the_default_is_used_for_the_run_but_not_written()
    {
        _data.Setting = null;
        _data.MostTraded.AddRange(Shares(3));

        var (listed, notes) = await Import.ListingAsync(Today, Now, Ct);

        listed.Symbols.Count.ShouldBe(7);
        _data.Setting.ShouldBeNull();
        notes.ShouldBeEmpty("a fresh database has no ranking yet: logged, not a warning");
    }

    [Fact]
    public async Task A_stored_setting_is_used_as_it_is_and_never_recomputed()
    {
        _data.Setting = """["NSE:NIFTY", "bad symbol!"]""";
        _data.MostTraded.AddRange(Shares(30));

        var (listed, notes) = await Import.ListingAsync(Today, Now, Ct);

        listed.Symbols.ShouldBe(["NSE:NIFTY"]);
        _data.Setting.ShouldBe("""["NSE:NIFTY", "bad symbol!"]""");
        notes.ShouldHaveSingleItem().ShouldContain("not a symbol");
    }

    [Fact]
    public async Task A_setting_that_cannot_be_read_costs_the_options_only()
    {
        _data.ReadFailure = new InvalidOperationException("database unavailable");

        var (listed, notes) = await Import.ListingAsync(Today, Now, Ct);

        listed.IsEmpty.ShouldBeTrue();
        notes.ShouldHaveSingleItem().ShouldStartWith("Option contracts were not updated");
    }

    [Fact]
    public async Task Rows_are_resolved_to_their_underlying_an_unknown_underlying_is_reported_and_its_rows_counted_unmapped()
    {
        _data.Underlyings["NSE:NIFTY"] = Nifty;
        _data.Result = new(2, 0, 0, 0, 3, 1);
        var listed = OptionUnderlyings.From(["NSE:NIFTY", "NSE:FINNIFTY"]);
        var parse = new OptionParse([Row(1, "NSE:NIFTY"), Row(2, "NSE:NIFTY"), Row(3, "NSE:FINNIFTY")], 40, 2, null);

        var notes = await Import.ApplyAsync(parse, listed, Today, Ct);

        var applied = _data.Applied.ShouldHaveSingleItem();
        applied.Contracts.Select(c => (c.SecurityId, c.UnderlyingId)).ShouldBe([(1L, Nifty), (2L, Nifty)]);
        applied.Lots.ShouldBe([new XDrishti.Application.Abstractions.Options.LotObservation(Nifty, 65)]);
        applied.Seeds.ShouldBe(LotSizeSeed.All);
        applied.Today.ShouldBe(Today);
        notes.Count.ShouldBe(2);
        notes[0].ShouldBe("Option underlyings not in the master list as an active index or share (1, unmapped_underlying 1 rows): NSE:FINNIFTY.");
        notes[1].ShouldContain("2 rows of listed underlyings could not be read (invalid)");
        notes.ShouldAllBe(n => n.Length <= XDrishti.Domain.Market.RefreshRun.MaxWarningLength);
    }

    [Fact]
    public async Task A_normal_import_has_no_warning_and_reused_ids_and_conflicts_are_reported()
    {
        _data.Underlyings["NSE:NIFTY"] = Nifty;
        var parse = new OptionParse([Row(1, "NSE:NIFTY")], 40, 0, null);
        var listed = OptionUnderlyings.From(["NSE:NIFTY"]);

        (await Import.ApplyAsync(parse, listed, Today, Ct)).ShouldBeEmpty();

        _data.Result = new(0, 0, 2, 3, 0, 0);
        var notes = await Import.ApplyAsync(parse, listed, Today, Ct);

        notes.Count.ShouldBe(2);
        notes[0].ShouldContain("3 Dhan security ids now name a different contract");
        notes[1].ShouldContain("2 rows name a contract another security id already holds");
    }

    [Fact]
    public async Task An_empty_list_or_no_parse_writes_nothing_and_says_nothing()
    {
        (await Import.ApplyAsync(null, OptionUnderlyings.From(["NSE:NIFTY"]), Today, Ct)).ShouldBeEmpty();
        (await Import.ApplyAsync(new OptionParse([Row(1, "NSE:NIFTY")], 0, 0, null), OptionUnderlyings.Empty, Today, Ct)).ShouldBeEmpty();
        _data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_without_the_option_columns_is_reported_and_nothing_is_written()
    {
        var notes = await Import.ApplyAsync(new OptionParse([], 0, 0, "Dhan's file has no column OPTION_TYPE; no option contract was read."), OptionUnderlyings.From(["NSE:NIFTY"]), Today, Ct);

        notes.ShouldHaveSingleItem().ShouldContain("OPTION_TYPE");
        _data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_store_becomes_a_warning_never_an_exception()
    {
        _data.Underlyings["NSE:NIFTY"] = Nifty;
        _data.ApplyFailure = new InvalidOperationException("permission denied");

        var notes = await Import.ApplyAsync(new OptionParse([Row(1, "NSE:NIFTY")], 0, 0, null), OptionUnderlyings.From(["NSE:NIFTY"]), Today, Ct);

        notes.ShouldHaveSingleItem().ShouldStartWith("Option contracts were not updated");
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _data.ReadFailure = new OperationCanceledException(cancelled.Token);

        await Should.ThrowAsync<OperationCanceledException>(async () => await Import.ListingAsync(Today, Now, cancelled.Token));
    }
}
