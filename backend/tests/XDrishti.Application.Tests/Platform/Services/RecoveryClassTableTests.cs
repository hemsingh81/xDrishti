using System.ComponentModel.DataAnnotations;
using System.Reflection;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>
/// The recovery class of every registered service (ADR 0240, spec AU-01 criterion 10): declared explicitly, and pinned here so a new service or a changed class is a deliberate edit of this table.
/// Anything that needs a live decision-time observation is <see cref="RecoveryClass.TimeBound"/>.
/// </summary>
public sealed class RecoveryClassTableTests
{
    private static readonly Dictionary<string, RecoveryClass> Expected = new(StringComparer.Ordinal)
    {
        ["eod"] = RecoveryClass.Convergent,
        ["history-eod"] = RecoveryClass.Convergent,
        ["options-backfill"] = RecoveryClass.Convergent,
        ["options-quality"] = RecoveryClass.Convergent,
        ["shadow-grade"] = RecoveryClass.Convergent,
        ["instrument-master"] = RecoveryClass.LatestOnly,
        ["filings-ingest"] = RecoveryClass.LatestOnly,
        ["portfolio-broker-history"] = RecoveryClass.LatestOnly,
        ["nightly"] = RecoveryClass.LatestOnly,
        ["suggestions"] = RecoveryClass.LatestOnly,
        ["briefing"] = RecoveryClass.LatestOnly,
        ["plan-expiry"] = RecoveryClass.LatestOnly,
        ["post-trade-prompts"] = RecoveryClass.LatestOnly,
        ["weekly"] = RecoveryClass.LatestOnly,
        ["weekly-review"] = RecoveryClass.LatestOnly,
        ["proposals"] = RecoveryClass.LatestOnly,
        ["learned-settings"] = RecoveryClass.LatestOnly,
        ["paper-review"] = RecoveryClass.LatestOnly,
        ["decay-monitor"] = RecoveryClass.TimeBound,
        ["readiness-snapshot"] = RecoveryClass.TimeBound,
        ["ml-monitor"] = RecoveryClass.TimeBound,
        ["shadow-digest"] = RecoveryClass.LatestOnly,
        ["preopen"] = RecoveryClass.TimeBound,
        ["review-cutoff"] = RecoveryClass.TimeBound,
        ["shadow-write"] = RecoveryClass.TimeBound,
        ["paper-capture"] = RecoveryClass.TimeBound,
        ["live-estimates"] = RecoveryClass.TimeBound,
        ["live-capture"] = RecoveryClass.TimeBound,
        ["live-settle"] = RecoveryClass.TimeBound,
        ["live-feed"] = RecoveryClass.TimeBound,
        ["holdings-quotes"] = RecoveryClass.TimeBound,
        ["tick-recorder"] = RecoveryClass.TimeBound,
        ["depth-recorder"] = RecoveryClass.TimeBound,
        ["option-chain-recorder"] = RecoveryClass.TimeBound,
    };

    private static List<ServiceDefinition> Registered() =>
        typeof(XDrishti.Application.DependencyInjection).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(XDrishti.Application.Abstractions.Platform.IScheduledService).IsAssignableFrom(t))
            .Select(t => t.GetField("Definition", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ServiceDefinition)
            .OfType<ServiceDefinition>()
            .ToList();

    [Fact]
    public void Every_registered_service_declares_its_recovery_class_explicitly()
    {
        var definitions = Registered();

        definitions.ShouldNotBeEmpty();
        definitions.Where(d => d.Constraints.RecoveryClass is null).Select(d => d.Id).ShouldBeEmpty();
    }

    [Fact]
    public void The_class_of_every_service_is_the_pinned_one_and_no_service_is_missing_from_the_table()
    {
        var actual = Registered().ToDictionary(d => d.Id, d => d.Constraints.RecoveryClass!.Value, StringComparer.Ordinal);

        actual.OrderBy(a => a.Key, StringComparer.Ordinal).ShouldBe(Expected.OrderBy(a => a.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void No_registered_service_is_dated_until_the_first_start_baseline_is_settled()
    {
        // A Dated service would run once per missed session; with no baseline for "registered since" a never-run service would replay the whole reach (AU-02 settles it first).
        Registered().Where(d => d.Constraints.RecoveryClass == RecoveryClass.Dated).ShouldBeEmpty();
    }

    [Fact]
    public void Planning_over_the_real_definitions_orders_data_then_nightly_then_suggestions()
    {
        var inputs = Registered().Select(d => new RecoveryInput(d, d.Defaults with { Enabled = true, CatchUp = true }, null)).ToList();
        var calendar = new XDrishti.Domain.Market.Calendar.TradingCalendar();
        var now = new DateTimeOffset(2026, 10, 9, 23, 0, 0, TimeSpan.FromMinutes(330)); // Fri 23:00: the Monday cutoff is ahead

        var plan = RecoveryPlanner.Plan(inputs, calendar, now, 30).Value;
        var order = plan.Steps.Select(s => s.ServiceId).ToList();

        order.ShouldContain("eod");
        order.ShouldContain("portfolio-broker-history");
        order.ShouldContain("nightly");
        order.ShouldContain("suggestions");
        order.IndexOf("eod").ShouldBeLessThan(order.IndexOf("nightly"));
        order.IndexOf("portfolio-broker-history").ShouldBeLessThan(order.IndexOf("nightly"));
        order.IndexOf("nightly").ShouldBeLessThan(order.IndexOf("suggestions"));
        plan.Steps.Select(s => s.ServiceId).ShouldAllBe(id => Expected[id] != RecoveryClass.TimeBound);
    }

    [Fact]
    public void The_planner_names_the_real_end_of_day_and_nightly_services()
    {
        RecoveryPlanner.EodId.ShouldBe(XDrishti.Application.Market.Bars.Eod.EodService.Id);
        RecoveryPlanner.NightlyId.ShouldBe(XDrishti.Application.Planning.NightlyService.Id);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(250, true)]
    [InlineData(251, false)]
    [InlineData(-5, false)]
    public void Recovery_max_sessions_is_validated_from_1_to_250(int value, bool valid)
    {
        new RecoverySettings().MaxSessions.ShouldBe(30);
        var settings = new RecoverySettings { MaxSessions = value };
        Validator.TryValidateObject(settings, new ValidationContext(settings), null, true).ShouldBe(valid);
    }
}
