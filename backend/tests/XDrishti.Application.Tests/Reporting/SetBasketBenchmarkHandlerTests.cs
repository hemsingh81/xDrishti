#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Reporting;
using XDrishti.Application.Tests.LearnedSettings;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>BK-06c criteria 2 and 7: the owner's choice of the basket benchmark is validated, stored under its own key and audited; the portfolio benchmark is untouched.</summary>
public sealed class SetBasketBenchmarkHandlerTests
{
    private const string Owner = "owner:me@x.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPortfolioBenchmark _master = Substitute.For<IPortfolioBenchmark>();
    private readonly IBasketBenchmark _current = Substitute.For<IBasketBenchmark>();
    private readonly InMemoryEssentialStore _store = new();
    private readonly FakeAccountEvents _events = new();
    private readonly Guid _bank = Guid.NewGuid();

    public SetBasketBenchmarkHandlerTests()
    {
        _master.FindAsync(_bank, Arg.Any<CancellationToken>()).Returns(new ValueTask<BenchmarkInstrument?>(new BenchmarkInstrument(_bank, "NSE:NIFTY_BANK", "Nifty Bank", "index", false, true, true)));
        _current.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<BasketBenchmark>(new BasketBenchmark(null, true, "Nifty 500", "Nifty 500 is not in the instrument master")));
    }

    private SetBasketBenchmarkHandler Handler() => new(_master, _current, _store, _events, _events, new FakeTimeProvider(), NullLogger<SetBasketBenchmarkHandler>.Instance);

    [Fact]
    public async Task An_index_is_stored_under_the_basket_key_with_who_and_one_audit_row()
    {
        var result = await Handler().HandleAsync(new SetBasketBenchmarkCommand(_bank, Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ProfileIndexView(_bank, "NSE:NIFTY_BANK", false, "Nifty Bank"));
        var row = await _store.GetAsync(AppSettingKeys.BasketBenchmarkInstrument, Ct);
        (row!.Value, row.UpdatedBy).ShouldBe((_bank.ToString("D"), Owner));
        (await _store.GetAsync(AppSettingKeys.PortfolioBenchmarkInstrument, Ct)).ShouldBeNull();
        _events.Recorded.Single().Event.ShouldBe(AccountEventType.EssentialSettingChanged);
    }

    [Fact]
    public async Task The_index_already_in_force_changes_and_records_nothing()
    {
        _current.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<BasketBenchmark>(new BasketBenchmark(new BenchmarkInstrument(_bank, "NSE:NIFTY_BANK", "Nifty Bank", "index", false, true, true), false, "Nifty Bank", null)));

        var result = await Handler().HandleAsync(new SetBasketBenchmarkCommand(_bank, Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        _events.Recorded.ShouldBeEmpty();
        (await _store.GetAsync(AppSettingKeys.BasketBenchmarkInstrument, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task No_id_an_unknown_id_a_share_or_a_retired_index_is_refused_and_a_non_owner_is_forbidden()
    {
        var share = Guid.NewGuid();
        _master.FindAsync(share, Arg.Any<CancellationToken>()).Returns(new ValueTask<BenchmarkInstrument?>(new BenchmarkInstrument(share, "NSE:RELIANCE", "Reliance", "share", false, true, true)));
        var retired = Guid.NewGuid();
        _master.FindAsync(retired, Arg.Any<CancellationToken>()).Returns(new ValueTask<BenchmarkInstrument?>(new BenchmarkInstrument(retired, "NSE:OLD", "Old", "index", true, false, false)));

        foreach (var id in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), share, retired })
        {
            (await Handler().HandleAsync(new SetBasketBenchmarkCommand(id, Owner), Ct)).Error.Code.ShouldBe("reports.invalid");
        }

        (await Handler().HandleAsync(new SetBasketBenchmarkCommand(_bank, "unknown"), Ct)).Error.Code.ShouldBe("settings.owner_only");
        _events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public void The_setting_is_in_the_catalog_as_an_advanced_stored_setting_with_its_default()
    {
        var d = XDrishti.Application.SettingsCatalog.SettingsRegistry.All.Single(x => x.Key == AppSettingKeys.BasketBenchmarkInstrument);

        (d.Tier, d.Default).ShouldBe((XDrishti.Application.SettingsCatalog.SettingTier.Advanced, "Nifty 500"));
    }
}
