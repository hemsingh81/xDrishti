using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>The detection over stored day bars: it only suggests, never twice, never where the owner has decided, and looks only at the days that changed.</summary>
public sealed class CorporateActionDetectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryActionData _data = new();
    private readonly FakeDailySeries _series = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CorporateActionDetection Detection => new(_series, _data, _data, _clock);

    private static DateOnly Day(List<DailyBar> bars, int index) => bars[index].Date;

    [Fact]
    public async Task A_split_in_the_series_becomes_a_suggestion_that_changes_nothing()
    {
        var bars = DaySeries.WithGap(80, 50, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);

        var found = await Detection.DetectAsync(id, null, Ct);

        found.ShouldBe(new InstrumentDetection(1, 1));
        var suggestion = _data.Actions.ShouldHaveSingleItem();
        (suggestion.InstrumentId, suggestion.ExDate, suggestion.Kind, suggestion.Ratio, suggestion.Status, suggestion.Source, suggestion.DecidedAt, suggestion.CreatedAt)
            .ShouldBe((id, Day(bars, 50), CorporateActionKind.Split, "1:5", CorporateActionStatus.Suggested, CorporateActionSource.Detected, null, Now));
        suggestion.Note!.ShouldContain("Could also be bonus 4:1.");
        (await _data.ListAppliedAsync(id, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Running_it_again_suggests_nothing_new()
    {
        var id = _series.Add("NSE:ABB", DaySeries.WithGap(80, 50, 0.2, 5.0));
        await Detection.DetectAsync(id, null, Ct);

        var again = await Detection.DetectAsync(id, null, Ct);

        again.ShouldBe(new InstrumentDetection(1, 0));
        _data.Actions.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(CorporateActionStatus.Applied)]
    [InlineData(CorporateActionStatus.Ignored)]
    public async Task A_date_that_has_an_action_is_not_suggested_whatever_its_status_or_kind(CorporateActionStatus status)
    {
        var bars = DaySeries.WithGap(80, 50, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);
        var existing = CorporateAction.Apply(id, Day(bars, 50), CorporateActionKind.Bonus, 4, 1, CorporateActionSource.Manual, null, "owner", Now);
        if (status == CorporateActionStatus.Ignored)
        {
            existing.Ignore("owner", Now);
        }

        _data.Actions.Add(existing);

        var found = await Detection.DetectAsync(id, null, Ct);

        found.ShouldBe(new InstrumentDetection(1, 0));
        _data.Actions.ShouldHaveSingleItem().Status.ShouldBe(status);
    }

    [Fact]
    public async Task A_suggestion_the_owner_moved_to_another_ex_date_is_not_suggested_again_on_the_date_it_was_found()
    {
        var bars = DaySeries.WithGap(80, 50, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);
        await Detection.DetectAsync(id, null, Ct);
        var suggestion = _data.Actions.Single();
        suggestion.Confirm(Day(bars, 51), suggestion.Kind, suggestion.Numerator, suggestion.Denominator, null, "owner", Now); // the owner corrected the ex-date

        var again = await Detection.DetectAsync(id, null, Ct);

        again.ShouldBe(new InstrumentDetection(1, 0));
        _data.Actions.ShouldHaveSingleItem().ExDate.ShouldBe(Day(bars, 51));
    }

    [Fact]
    public async Task After_a_job_only_the_changed_days_are_looked_at_with_the_sessions_before_them_as_context()
    {
        var bars = DaySeries.WithGap(80, 50, 0.2, 5.0);
        var id = _series.Add("NSE:ABB", bars);

        var before = await Detection.DetectAsync(id, Day(bars, 51), Ct);
        var including = await Detection.DetectAsync(id, Day(bars, 50), Ct);

        before.ShouldBe(new InstrumentDetection(0, 0));
        including.ShouldBe(new InstrumentDetection(1, 1));
    }

    [Fact]
    public async Task A_calm_series_and_an_instrument_without_bars_suggest_nothing()
    {
        var calm = _series.Add("NSE:ABB", DaySeries.Calm(120));

        (await Detection.DetectAsync(calm, null, Ct)).ShouldBe(new InstrumentDetection(0, 0));
        (await Detection.DetectAsync(Guid.NewGuid(), null, Ct)).ShouldBe(new InstrumentDetection(0, 0));
        _data.Actions.ShouldBeEmpty();
    }

    [Fact]
    public async Task On_request_every_instrument_with_bars_is_looked_at_over_its_whole_history()
    {
        _series.Add("NSE:ABB", DaySeries.WithGap(80, 50, 0.2, 5.0));
        _series.Add("NSE:TCS", DaySeries.WithGap(80, 30, 0.5, 3.0));
        _series.Add("NSE:INFY", DaySeries.Calm(80));

        var totals = await Detection.DetectAllAsync(Ct);

        totals.ShouldBe(new DetectionTotals(3, 2, 2));
        _data.Actions.Select(a => a.Ratio).Order().ShouldBe(["1:2", "1:5"]);
    }

    [Fact]
    public async Task The_detect_command_reports_the_counts_and_the_suggestions_waiting()
    {
        _series.Add("NSE:ABB", DaySeries.WithGap(80, 50, 0.2, 5.0));
        var handler = new DetectCorporateActionsHandler(Detection, _data, NullLogger<DetectCorporateActionsHandler>.Instance);

        var first = await handler.HandleAsync(new DetectCorporateActionsCommand(), Ct);
        var second = await handler.HandleAsync(new DetectCorporateActionsCommand(), Ct);

        first.Value.ShouldBe(new DetectionResult(1, 1, 1, 1));
        second.Value.ShouldBe(new DetectionResult(1, 1, 0, 1));
        _data.Events.ShouldBeEmpty(); // a suggestion is not a decision
    }
}
