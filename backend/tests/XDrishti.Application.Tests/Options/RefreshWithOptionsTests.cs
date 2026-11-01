using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Tests.Market;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Options;

/// <summary>Spec P14-01 criteria 1 and 2 through the refresh: the option step adds the contracts of the listed underlyings and leaves the master list's candidates and counts as they were.</summary>
public sealed class RefreshWithOptionsTests
{
    private static readonly Guid Reliance = Guid.Parse("00000000-0000-7000-8000-000000002885");

    private readonly MarketFixture _f = new();

    [Fact]
    public async Task The_listed_underlyings_contracts_are_applied_after_the_master_list_whose_candidates_do_not_change()
    {
        var plain = new MarketFixture();
        var before = await plain.RunAsync(plain.Register());
        _f.Options.Setting = """["NSE:RELIANCE", "NSE:NIFTY"]""";
        _f.Options.Underlyings["NSE:RELIANCE"] = Reliance;

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        _f.Data.Applied.Single().Candidates.ShouldBe(plain.Data.Applied.Single().Candidates);
        (run.SkippedByScope, run.RowsRead).ShouldBe((before.SkippedByScope, before.RowsRead));
        run.KeptByType.ShouldBe(before.KeptByType);
        var applied = _f.Options.Applied.ShouldHaveSingleItem();
        applied.Contracts.Select(c => c.SecurityId).Order().ShouldBe([106474L, 106475, 106476]);
        applied.Contracts.ShouldAllBe(c => c.UnderlyingId == Reliance);
        applied.Today.ShouldBe(IndiaTime.Today(MasterFixtures.Now));
        run.Warnings.ShouldHaveSingleItem().ShouldStartWith("Option underlyings not in the master list as an active index or share (1, unmapped_underlying 0 rows): NSE:NIFTY.");
    }

    [Fact]
    public async Task A_failing_option_store_leaves_the_refresh_succeeded_with_a_warning()
    {
        _f.Options.Setting = """["NSE:RELIANCE"]""";
        _f.Options.Underlyings["NSE:RELIANCE"] = Reliance;
        _f.Options.ApplyFailure = new InvalidOperationException("permission denied");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        _f.Data.Applied.ShouldHaveSingleItem();
        run.Warnings.ShouldContain(w => w.StartsWith("Option contracts were not updated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_normal_option_import_leaves_the_warnings_identical_to_a_run_without_a_list()
    {
        var plain = new MarketFixture();
        var before = await plain.RunAsync(plain.Register());
        _f.Options.Setting = """["NSE:RELIANCE"]""";
        _f.Options.Underlyings["NSE:RELIANCE"] = Reliance;

        var run = await _f.RunAsync(_f.Register());

        _f.Options.Applied.ShouldHaveSingleItem().Contracts.Count.ShouldBe(3);
        run.Warnings.ShouldBe(before.Warnings);
    }

    [Fact]
    public async Task Option_problems_come_first_so_the_warning_cap_never_drops_them()
    {
        _f.Options.Setting = """["NSE:RELIANCE"]""";
        _f.Options.Underlyings["NSE:RELIANCE"] = Reliance;
        _f.Options.Result = new(0, 0, 0, 1, 0, 0);
        _f.Source.Sectors = () => SourceDownload.Failed("blocked");

        var run = await _f.RunAsync(_f.Register());

        run.Warnings[0].ShouldContain("reused_id");
    }

    [Fact]
    public async Task An_empty_list_writes_no_contract_and_adds_no_warning()
    {
        var run = await _f.RunAsync(_f.Register());

        _f.Options.Applied.ShouldBeEmpty();
        run.Warnings.ShouldBeEmpty();
    }
}
