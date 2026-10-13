using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>Reading and choosing the benchmark index (acceptance criterion 8 of P2-07).</summary>
public sealed class BenchmarkSettingHandlerTests
{
    private const string Owner = "owner@example.invalid";

    private readonly FakePortfolioBenchmark _benchmarks = new();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    private readonly BenchmarkInstrument _nifty;

    public BenchmarkSettingHandlerTests()
    {
        _nifty = _benchmarks.Add("NSE:NIFTY", "Nifty 50", tracked: true, hasBars: true);
        _benchmarks.Default = _nifty.Id;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SetBenchmarkHandler Set() => new(_benchmarks, _benchmarks, _benchmarks, _clock, NullLogger<SetBenchmarkHandler>.Instance);

    private Task<Result<BenchmarkView>> SetAsync(Guid? id) => Set().HandleAsync(new SetBenchmarkCommand(new BenchmarkInput(id), Owner), Ct).AsTask();

    [Fact]
    public async Task Until_one_is_chosen_the_default_index_applies_and_is_flagged_as_default()
    {
        var view = await new GetBenchmarkHandler(_benchmarks).HandleAsync(new GetBenchmarkQuery(), Ct);

        view.ShouldBe(new BenchmarkView(_nifty.Id, "NSE:NIFTY", "Nifty 50", true, true, true));
    }

    [Fact]
    public async Task Without_any_such_index_in_the_master_the_view_is_empty_and_default()
    {
        _benchmarks.Default = null;

        var view = await new GetBenchmarkHandler(_benchmarks).HandleAsync(new GetBenchmarkQuery(), Ct);

        view.ShouldBe(new BenchmarkView(null, null, null, true, false, false));
    }

    [Fact]
    public async Task Any_listed_index_can_be_chosen_and_the_change_is_audited_with_the_symbols()
    {
        var bank = _benchmarks.Add("NSE:NIFTY_BANK", "Nifty Bank");

        var result = await SetAsync(bank.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new BenchmarkView(bank.Id, "NSE:NIFTY_BANK", "Nifty Bank", false, false, false));
        var audit = _benchmarks.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.PortfolioBenchmarkChanged, Owner, null));
        audit.Detail.ShouldBe("Portfolio benchmark changed from NSE:NIFTY to NSE:NIFTY_BANK");
        audit.At.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Choosing_the_index_already_in_force_changes_and_records_nothing()
    {
        var bank = _benchmarks.Add("NSE:NIFTY_BANK", "Nifty Bank");
        await SetAsync(bank.Id);

        var again = await SetAsync(bank.Id);

        again.IsSuccess.ShouldBeTrue();
        _benchmarks.Events.Count.ShouldBe(1);
        _benchmarks.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Choosing_the_default_explicitly_is_stored_so_it_stays_when_the_default_changes()
    {
        var result = await SetAsync(_nifty.Id);

        result.Value.IsDefault.ShouldBeFalse();
        _benchmarks.Chosen.ShouldBe(_nifty.Id);
        _benchmarks.Events.ShouldHaveSingleItem().Detail.ShouldBe("Portfolio benchmark changed from NSE:NIFTY to NSE:NIFTY");
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "share", "Pick an index" },
        { "etf", "Pick an index" },
        { "future", "Pick an index" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task An_instrument_that_is_not_an_index_is_portfolio_invalid(string type, string message)
    {
        var share = _benchmarks.Add("NSE:RELIANCE", "Reliance", type);

        var result = await SetAsync(share.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("instrumentId");
        result.Error.Fields![0].Message.ShouldStartWith(message);
        _benchmarks.Chosen.ShouldBeNull();
        _benchmarks.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_retired_missing_or_empty_id_is_portfolio_invalid()
    {
        var retired = _benchmarks.Add("NSE:OLD_INDEX", "Old index", retired: true);

        foreach (var id in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), retired.Id })
        {
            var result = await SetAsync(id);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe("portfolio.invalid");
        }

        _benchmarks.Saves.ShouldBe(0);
        _benchmarks.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_collides_with_another_change_is_a_conflict()
    {
        var bank = _benchmarks.Add("NSE:NIFTY_BANK", "Nifty Bank");
        _benchmarks.RefuseNextSave = true;

        var result = await SetAsync(bank.Id);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.conflict");
        _benchmarks.Chosen.ShouldBeNull();
    }

    [Fact]
    public async Task A_chosen_index_that_has_left_the_master_falls_back_to_the_default()
    {
        _benchmarks.Chosen = Guid.NewGuid();

        var view = await new GetBenchmarkHandler(_benchmarks).HandleAsync(new GetBenchmarkQuery(), Ct);

        view.InstrumentId.ShouldBe(_nifty.Id);
        view.IsDefault.ShouldBeTrue();
    }
}
