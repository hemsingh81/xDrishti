using Microsoft.EntityFrameworkCore;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Portfolio;

/// <summary>Which saves tell the live feed that its active set may have changed (P2-10 criterion 8): decided from the change tracker alone, so no database is touched.</summary>
public sealed class ActiveSetSignalTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly XDrishtiDbContext _db;

    public ActiveSetSignalTests()
    {
        var options = new DbContextOptionsBuilder<XDrishtiDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=never_opened;Username=nobody");
        _db = new XDrishtiDbContext(options.Options);
    }

    public void Dispose() => _db.Dispose();

    private static Holding NewHolding() =>
        Holding.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Alpha Industries", new BrokerHolding("NSE", "1001", "INE000A01011", "ALPHA", 100, 50m, 60m), new DateOnly(2024, 10, 12), false, LotSource.Import, Now);

    private static PriceAlert NewAlert() => PriceAlert.Create(Guid.NewGuid(), AlertDirection.AtOrAbove, 100m, null, "owner@example.invalid", Now);

    private bool Affected => ActiveSetSignal.IsAffectedBy(_db.ChangeTracker);

    [Fact]
    public void Nothing_collected_changes_nothing()
    {
        Affected.ShouldBeFalse();
    }

    [Fact]
    public void A_holding_added_changes_the_set()
    {
        _db.Add(NewHolding());

        Affected.ShouldBeTrue();
    }

    [Fact]
    public void A_holding_whose_state_or_instrument_changed_changes_the_set()
    {
        var closed = _db.Attach(NewHolding());
        closed.Property(h => h.Status).IsModified = true;
        Affected.ShouldBeTrue();

        _db.ChangeTracker.Clear();
        var moved = _db.Attach(NewHolding());
        moved.Property(h => h.InstrumentId).IsModified = true;
        Affected.ShouldBeTrue();
    }

    [Fact]
    public void A_holding_whose_price_or_quantity_changed_does_not()
    {
        var entry = _db.Attach(NewHolding());
        entry.Property(h => h.LastPrice).IsModified = true;
        entry.Property(h => h.Qty).IsModified = true;

        Affected.ShouldBeFalse();
    }

    [Fact]
    public void A_price_alert_added_or_whose_status_changed_changes_the_set()
    {
        _db.Add(NewAlert());
        Affected.ShouldBeTrue();

        _db.ChangeTracker.Clear();
        _db.Attach(NewAlert()).Property(a => a.Status).IsModified = true;
        Affected.ShouldBeTrue();

        _db.ChangeTracker.Clear();
        _db.Attach(NewAlert()).Property(a => a.Note).IsModified = true;
        Affected.ShouldBeFalse();
    }

    [Theory]
    [InlineData(AccountEventType.PriceAlertCreated, true)]
    [InlineData(AccountEventType.PriceAlertTriggered, true)]
    [InlineData(AccountEventType.PriceAlertCancelled, true)]
    [InlineData(AccountEventType.HoldingsSynced, false)]
    [InlineData(AccountEventType.ServicePaused, false)]
    [InlineData(AccountEventType.ActionSnoozed, false)]
    public void The_audit_events_of_price_alerts_stand_for_their_conditional_updates(AccountEventType type, bool expected)
    {
        _db.Add(AccountEvent.Record(type, null, "detail", "owner@example.invalid", Now));

        Affected.ShouldBe(expected);
    }
}
