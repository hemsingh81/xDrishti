using System.Text.Json;
using XDrishti.Domain.Platform.Services;
using static XDrishti.Domain.Tests.Platform.Services.ServiceTestData;

namespace XDrishti.Domain.Tests.Platform.Services;

/// <summary>The run record, the stored choices of a service and the small value types around them.</summary>
public sealed class ServiceEntitiesTests
{
    // ---- ServiceRun ----

    [Fact]
    public void A_started_run_is_running_with_its_trigger_and_attempt()
    {
        var run = ServiceRun.Start("eod", ServiceRunTrigger.CatchUp, 2, At(10, 12, 15, 50));

        (run.ServiceId, run.Trigger, run.Attempt, run.Status, run.IsRunning, run.FinishedAt, run.Message).ShouldBe(("eod", ServiceRunTrigger.CatchUp, 2, ServiceRunStatus.Running, true, null, null));
    }

    [Fact]
    public void An_attempt_below_one_is_one()
    {
        ServiceRun.Start("eod", ServiceRunTrigger.Manual, 0, Stamp).Attempt.ShouldBe(1);
    }

    [Fact]
    public void A_run_ends_succeeded_failed_or_skipped_with_the_time_and_the_message()
    {
        var done = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, Stamp);
        done.Succeed(Stamp.AddMinutes(1), "fine");
        var failed = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, Stamp);
        failed.Fail(Stamp.AddMinutes(2), "broken");
        var skipped = ServiceRun.Skipped("eod", ServiceRunTrigger.Scheduled, "busy", Stamp);

        (done.Status, done.Message, done.FinishedAt, done.IsRunning).ShouldBe((ServiceRunStatus.Succeeded, "fine", Stamp.AddMinutes(1), false));
        (failed.Status, failed.Message).ShouldBe((ServiceRunStatus.Failed, "broken"));
        (skipped.Status, skipped.Message, skipped.FinishedAt).ShouldBe((ServiceRunStatus.Skipped, "busy", Stamp));
    }

    [Fact]
    public void A_data_job_can_be_linked_to_a_run()
    {
        var run = ServiceRun.Start("eod", ServiceRunTrigger.Scheduled, 1, Stamp);
        var job = Guid.NewGuid();

        run.LinkDataJob(job);

        run.DataJobId.ShouldBe(job);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" one\r\ntwo ", "one two")]
    public void The_message_is_trimmed_to_one_line(string? message, string? expected)
    {
        ServiceRun.Limit(message).ShouldBe(expected);
    }

    [Fact]
    public void A_long_message_is_cut_to_the_column_size()
    {
        ServiceRun.Limit(new string('x', 900))!.Length.ShouldBe(ServiceRun.MaxMessageLength);
    }

    [Theory]
    [InlineData(ServiceRunTrigger.Scheduled, "scheduled")]
    [InlineData(ServiceRunTrigger.CatchUp, "catch_up")]
    [InlineData(ServiceRunTrigger.After, "after")]
    [InlineData(ServiceRunTrigger.Manual, "manual")]
    public void Triggers_have_their_codes_both_ways(ServiceRunTrigger trigger, string code)
    {
        trigger.ToCode().ShouldBe(code);
        ServiceRunCodes.ParseTrigger(code).ShouldBe(trigger);
    }

    // ---- ServiceSchedule ----

    [Fact]
    public void A_row_holds_the_settings_it_was_created_with_and_who_changed_it()
    {
        var settings = Settings(Schedule.Daily(T(15, 50)), retries: 2, timeout: 90);

        var row = ServiceSchedule.Create("eod", settings, "  owner@example.invalid ", Stamp);

        row.Id.ShouldBe("eod");
        row.Settings.Same(settings).ShouldBeTrue();
        (row.UpdatedBy, row.UpdatedAt, row.Revision).ShouldBe(("owner@example.invalid", Stamp, 0u));
    }

    [Fact]
    public void Applying_settings_replaces_every_choice()
    {
        var row = ServiceSchedule.Create("eod", Settings(Schedule.Daily(T(15, 50))), "a", Stamp);
        var next = new ServiceSettings(false, Schedule.Weekly(DayOfWeek.Saturday, T(10, 0)), 4, 120, false, false, new Dictionary<string, JsonElement> { ["x"] = JsonSerializer.SerializeToElement(1) });

        row.Apply(next, "b", Stamp.AddHours(1));

        row.Settings.Same(next).ShouldBeTrue();
        (row.UpdatedBy, row.UpdatedAt).ShouldBe(("b", Stamp.AddHours(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_actor_is_recorded_as_unknown(string actor)
    {
        ServiceSchedule.Create("eod", Settings(Schedule.Daily(T(15, 50))), actor, Stamp).UpdatedBy.ShouldBe("unknown");
    }

    [Fact]
    public void Settings_compare_their_parameters_by_value()
    {
        var a = Settings(Schedule.Daily(T(15, 50))) with { Parameters = new Dictionary<string, JsonElement> { ["n"] = JsonSerializer.SerializeToElement(5) } };
        var same = a with { Parameters = new Dictionary<string, JsonElement> { ["n"] = JsonSerializer.SerializeToElement(5) } };
        var other = a with { Parameters = new Dictionary<string, JsonElement> { ["n"] = JsonSerializer.SerializeToElement(6) } };

        a.Same(same).ShouldBeTrue();
        a.Same(other).ShouldBeFalse();
        a.Same(null).ShouldBeFalse();
        a.Same(a with { Retries = 1 }).ShouldBeFalse();
    }

    // ---- days and parameters ----

    [Fact]
    public void Days_go_to_codes_and_back_and_an_unknown_code_is_refused()
    {
        WeekdaySets.TryParse(["mon", "wed"], out var set).ShouldBeTrue();

        set.ToCodes().ShouldBe(["mon", "wed"]);
        WeekdaySets.TryParse(["mon", "xyz"], out _).ShouldBeFalse();
        WeekdaySets.TryParse(null, out var none).ShouldBeTrue();
        none.ShouldBe(Weekdays.None);
        Weekdays.EveryDay.Includes(DayOfWeek.Sunday).ShouldBeTrue();
        Weekdays.MondayToFriday.Includes(DayOfWeek.Saturday).ShouldBeFalse();
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("1", true)]
    [InlineData("30", true)]
    [InlineData("0", false)]
    [InlineData("31", false)]
    [InlineData("2.5", false)]
    [InlineData("\"5\"", false)]
    [InlineData("true", false)]
    public void A_whole_number_parameter_accepts_whole_numbers_inside_its_bounds(string json, bool accepted)
    {
        var parameter = ParameterDefinition.Whole("n", "N", 1, 30, 5);

        parameter.Accepts(JsonDocument.Parse(json).RootElement, out var problem).ShouldBe(accepted);
        (problem.Length > 0).ShouldBe(!accepted);
    }

    [Fact]
    public void A_service_without_a_parameter_given_gets_the_schema_default()
    {
        var definition = Definition("eod", Schedule.Daily(T(15, 50)), parameters: [ParameterDefinition.Whole("n", "N", 1, 30, 5), ParameterDefinition.Switch("s", "S", true)]);

        var filled = definition.WithDefaults(new Dictionary<string, JsonElement> { ["n"] = JsonSerializer.SerializeToElement(9) });

        (filled["n"].GetInt32(), filled["s"].GetBoolean()).ShouldBe((9, true));
    }

    [Fact]
    public void Time_ranges_include_both_ends()
    {
        TimeRange.MarketHours.Contains(T(9, 15)).ShouldBeTrue();
        TimeRange.MarketHours.Contains(T(15, 30)).ShouldBeTrue();
        TimeRange.MarketHours.Contains(T(15, 31)).ShouldBeFalse();
        TimeRange.MarketHours.Intersects(T(15, 30), T(16, 0)).ShouldBeTrue();
        TimeRange.MarketHours.Intersects(T(15, 31), T(16, 0)).ShouldBeFalse();
    }
}
