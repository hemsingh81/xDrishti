using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Listing and cancelling alerts (acceptance criterion 14): status filter, newest first, paging; cancel is idempotent, a triggered alert stays triggered, nothing is deleted.</summary>
public sealed class PriceAlertListAndCancelTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ActionsHarness _harness = new();

    private PriceAlert Seed(string symbol, decimal level, int minutesAgo)
    {
        var instrument = _harness.Data.AddInstrument(symbol);
        return _harness.Data.SeedAlert(instrument.Id, AlertDirection.AtOrBelow, level, _harness.Now.AddMinutes(-minutesAgo));
    }

    private async Task<Result<PriceAlertPage>> ListAsync(string? status = null, int? page = null, int? pageSize = null) =>
        await _harness.Alerts().HandleAsync(new ListPriceAlertsQuery(status, page, pageSize), Ct);

    private async Task<Result<PriceAlertView>> CancelAsync(Guid id) =>
        await _harness.Cancel().HandleAsync(new CancelPriceAlertCommand(id, ActionsHarness.Owner), Ct);

    [Fact]
    public async Task The_newest_alert_comes_first()
    {
        Seed("OLD", 10m, 30);
        Seed("NEW", 20m, 1);
        Seed("MID", 30m, 10);

        var page = (await ListAsync()).Value;

        page.Items.Select(a => a.Symbol).ShouldBe(["NEW", "MID", "OLD"]);
        (page.Page, page.PageSize, page.Total, page.ActiveCount, page.ActiveLimit).ShouldBe((1, 50, 3, 3, 200));
    }

    [Fact]
    public async Task The_status_filter_picks_active_triggered_cancelled_or_all()
    {
        var active = Seed("A", 10m, 3);
        var triggered = Seed("T", 20m, 2);
        var cancelled = Seed("C", 30m, 1);
        await _harness.Data.TryTriggerAsync(triggered.Id, 19m, _harness.Now, Ct);
        await CancelAsync(cancelled.Id);

        (await ListAsync("active")).Value.Items.Select(a => a.Id).ShouldBe([active.Id]);
        (await ListAsync("triggered")).Value.Items.Select(a => (a.Id, a.Status, a.TriggerPrice)).ShouldBe([(triggered.Id, "triggered", (decimal?)19m)]);
        (await ListAsync("cancelled")).Value.Items.Select(a => a.Id).ShouldBe([cancelled.Id]);
        (await ListAsync("all")).Value.Total.ShouldBe(3);
        (await ListAsync()).Value.Total.ShouldBe(3);
        (await ListAsync("active")).Value.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Alerts_are_paged_without_overlap()
    {
        for (var i = 0; i < 5; i++)
        {
            Seed("S" + i, 10m + i, i);
        }

        var first = (await ListAsync(page: 1, pageSize: 2)).Value;
        var second = (await ListAsync(page: 2, pageSize: 2)).Value;
        var third = (await ListAsync(page: 3, pageSize: 2)).Value;

        first.Items.Concat(second.Items).Concat(third.Items).Select(a => a.Symbol).ShouldBe(["S0", "S1", "S2", "S3", "S4"]);
        (first.Total, third.Items.Count).ShouldBe((5, 1));
    }

    [Theory]
    [InlineData("pending", null, null, "status")]
    [InlineData(null, 0, null, "page")]
    [InlineData(null, null, 0, "pageSize")]
    [InlineData(null, null, 101, "pageSize")]
    public async Task Bad_parameters_are_field_errors(string? status, int? page, int? pageSize, string field)
    {
        var result = await ListAsync(status, page, pageSize);

        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe([field]);
    }

    [Fact]
    public async Task Cancelling_an_active_alert_cancels_it_and_audits_it()
    {
        var alert = Seed("INFY", 1200m, 1);

        var view = (await CancelAsync(alert.Id)).Value;

        view.Status.ShouldBe("cancelled");
        var audit = _harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.Detail).ShouldBe((AccountEventType.PriceAlertCancelled, ActionsHarness.Owner, "Price alert cancelled: INFY at or below ₹1,200"));
    }

    [Fact]
    public async Task Cancelling_twice_is_the_same_as_once()
    {
        var alert = Seed("INFY", 1200m, 1);
        await CancelAsync(alert.Id);

        var again = await CancelAsync(alert.Id);

        again.Value.Status.ShouldBe("cancelled");
        _harness.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_triggered_alert_stays_triggered_when_it_is_cancelled()
    {
        var alert = Seed("INFY", 1200m, 1);
        await _harness.Data.TryTriggerAsync(alert.Id, 1100m, _harness.Now, Ct);

        var view = (await CancelAsync(alert.Id)).Value;

        (view.Status, view.TriggerPrice).ShouldBe(("triggered", (decimal?)1100m));
        _harness.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_alert_is_not_found()
    {
        var result = await CancelAsync(Guid.NewGuid());

        (result.Error.Code, result.Error.Kind).ShouldBe(("portfolio.not_found", ErrorKind.NotFound));
    }

    [Fact]
    public async Task Cancelling_cannot_undo_a_trigger_that_happened_between_the_read_and_the_change()
    {
        var alert = Seed("INFY", 1200m, 1);
        // The cancel finds the alert active, but the conditional update loses against a trigger: nothing is recorded and the alert stays triggered.
        var racing = new TriggerBeforeCancel(_harness.Data, alert.Id, _harness.Now);

        var result = await new CancelPriceAlertHandler(racing, _harness.Data, _harness.Data, _harness.Clock, NullLogger<CancelPriceAlertHandler>.Instance)
            .HandleAsync(new CancelPriceAlertCommand(alert.Id, ActionsHarness.Owner), Ct);

        result.Value.Status.ShouldBe("triggered");
        _harness.Data.Events.ShouldBeEmpty();
    }

    /// <summary>The store, except that the alert triggers right after it was first read (as active).</summary>
    private sealed class TriggerBeforeCancel(InMemoryActionData inner, Guid alertId, DateTimeOffset now) : IPriceAlertStore
    {
        private bool _triggered;

        public async ValueTask<AlertRow?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            var row = await inner.FindAsync(id, cancellationToken);
            if (!_triggered)
            {
                _triggered = true;
                await inner.TryTriggerAsync(alertId, 1100m, now, cancellationToken);
            }

            return row;
        }

        public ValueTask<bool> TryCancelAsync(Guid id, CancellationToken cancellationToken) => inner.TryCancelAsync(id, cancellationToken);

        public ValueTask<AlertInstrument?> FindInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken) => inner.FindInstrumentAsync(instrumentId, cancellationToken);

        public ValueTask<KnownPrice?> GetKnownPriceAsync(Guid instrumentId, CancellationToken cancellationToken) => inner.GetKnownPriceAsync(instrumentId, cancellationToken);

        public ValueTask<int> CountActiveAsync(CancellationToken cancellationToken) => inner.CountActiveAsync(cancellationToken);

        public void Add(PriceAlert alert) => inner.Add(alert);

        public ValueTask<(IReadOnlyList<AlertRow> Items, int Total)> ListAsync(AlertStatus? status, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListAsync(status, page, pageSize, cancellationToken);

        public ValueTask<IReadOnlyList<AlertRow>> ListTriggeredSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
            inner.ListTriggeredSinceAsync(since, cancellationToken);
    }
}
