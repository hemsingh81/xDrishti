using XDrishti.Execution.Application;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Tests.Readiness;

/// <summary>The pure pieces of P12-11: the day mark rule, the schedule, the IP facts, the four settings with their risk directions, and the two tolerant Dhan parsers.</summary>
public sealed class ReadinessDomainTests
{
    private static readonly DateOnly Day = new(2026, 10, 27);

    [Fact]
    public void The_mark_is_on_time_inside_5_seconds_and_late_after_it_and_late_is_never_ready()
    {
        ReadinessDayMark.Decide(Ist.At(27, 9, 10, 30), Day, ReadinessState.Ready).ShouldBe(new(ReadinessState.Ready, false));
        ReadinessDayMark.Decide(Ist.At(27, 9, 10, 35), Day, ReadinessState.NotReady).ShouldBe(new(ReadinessState.NotReady, false));
        ReadinessDayMark.Decide(Ist.At(27, 9, 10, 36), Day, ReadinessState.Ready).ShouldBe(new(ReadinessState.Unavailable, true));
        ReadinessDayMark.IsDue(Ist.At(27, 9, 10, 29), Day).ShouldBeFalse();
        ReadinessDayMark.IsDue(Ist.At(27, 9, 10, 30), Day).ShouldBeTrue();
    }

    [Theory]
    [InlineData(7, 59, false)]
    [InlineData(8, 0, true)]
    [InlineData(16, 30, true)]
    [InlineData(16, 31, false)]
    public void The_reading_window_is_08_00_to_16_30_india_time(int hour, int minute, bool inside) => ReadinessSchedule.InWindow(Ist.At(27, hour, minute)).ShouldBe(inside);

    [Fact]
    public void Storing_follows_a_change_or_15_minutes()
    {
        ReadinessSchedule.ShouldStore(false, Ist.At(27, 9, 0), Ist.At(27, 9, 14, 59)).ShouldBeFalse();
        ReadinessSchedule.ShouldStore(false, Ist.At(27, 9, 0), Ist.At(27, 9, 15)).ShouldBeTrue();
        ReadinessSchedule.ShouldStore(true, Ist.At(27, 9, 0), Ist.At(27, 9, 1)).ShouldBeTrue();
        ReadinessSchedule.ShouldStore(false, null, Ist.At(27, 9, 1)).ShouldBeTrue();
    }

    [Fact]
    public void The_ip_can_be_changed_again_7_days_after_the_newest_change_and_the_same_address_written_two_ways_is_one()
    {
        var facts = new IpFacts("2001:0db8:0000:0000:0000:0000:0000:0001", "203.0.113.7", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9));

        facts.ModifyAfter.ShouldBe(new DateOnly(2026, 10, 16));
        facts.Configures("2001:db8::1").ShouldBeTrue();
        facts.Configures("203.0.113.8").ShouldBeFalse();
        facts.Configures("not an ip").ShouldBeFalse();
        new IpFacts(null, null, null, null).ModifyAfter.ShouldBeNull();
    }

    [Fact]
    public void The_four_readiness_settings_exist_with_defaults_ranges_and_risk_directions()
    {
        ExecSettingRegistry.Find("exec.ready_token_until")!.ShouldBe(new ExecSetting("exec.ready_token_until", ExecSettingKind.TimeOfDay, "16:45", "16:45", "18:00", ExecRiskDirection.LowerIsRiskier));
        ExecSettingRegistry.Find("exec.readiness_interval_s")!.ShouldBe(new ExecSetting("exec.readiness_interval_s", ExecSettingKind.Number, "300", "60", "900", ExecRiskDirection.HigherIsRiskier));
        ExecSettingRegistry.Find("exec.attestation_max_age_days")!.ShouldBe(new ExecSetting("exec.attestation_max_age_days", ExecSettingKind.Number, "30", "7", "90", ExecRiskDirection.HigherIsRiskier));
        ExecSettingRegistry.Find("exec.expected_static_ip")!.Kind.ShouldBe(ExecSettingKind.Text);
        ExecSettingRegistry.Find("exec.expected_static_ip")!.Default.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("exec.ready_token_until", "16:44", false)]
    [InlineData("exec.ready_token_until", "16:45", true)]
    [InlineData("exec.ready_token_until", "18:01", false)]
    [InlineData("exec.readiness_interval_s", "59", false)]
    [InlineData("exec.readiness_interval_s", "900", true)]
    [InlineData("exec.readiness_interval_s", "901", false)]
    [InlineData("exec.attestation_max_age_days", "6", false)]
    [InlineData("exec.attestation_max_age_days", "90", true)]
    [InlineData("exec.attestation_max_age_days", "91", false)]
    [InlineData("exec.expected_static_ip", "", true)]
    [InlineData("exec.expected_static_ip", "203.0.113.7", true)]
    [InlineData("exec.expected_static_ip", "2001:db8::1", true)]
    [InlineData("exec.expected_static_ip", "2001:0db8::1", false)] // not the normal form
    [InlineData("exec.expected_static_ip", "203.0.113", false)]
    [InlineData("exec.expected_static_ip", "example.com", false)]
    [InlineData("exec.expected_static_ip", "203.0.113.7:80", false)]
    [InlineData("exec.expected_static_ip", " 203.0.113.7", false)]
    public void Each_value_is_inside_its_range_or_refused(string key, string value, bool valid) => ExecSettingRegistry.IsValid(ExecSettingRegistry.Find(key)!, value).ShouldBe(valid);

    [Fact]
    public async Task The_settings_in_force_reach_the_loop_and_an_invalid_stored_value_falls_back_to_the_default()
    {
        var view = await new ExecSettingsReader(new Snapshot(new Dictionary<string, string> { ["exec.expected_static_ip"] = "203.0.113.7", ["exec.ready_token_until"] = "16:00", ["exec.readiness_interval_s"] = "120" })).ReadAsync(CancellationToken.None);

        var settings = ExecSettingsCache.ReadinessFrom(view);

        settings.ShouldBe(new ReadinessSettings(new TimeOnly(16, 45), 20, "203.0.113.7", 30, 120));
        view.Replaced.ShouldBe(["exec.ready_token_until"]);
    }

    private sealed class Snapshot(IReadOnlyDictionary<string, string>? values) : IExecSettingsSnapshots
    {
        public Task<IReadOnlyDictionary<string, string>?> NewestAsync(CancellationToken cancellationToken) => Task.FromResult(values);
    }

    [Fact]
    public void The_profile_is_reduced_to_its_token_validity_in_india_time_and_nothing_else_is_kept()
    {
        var read = DhanReads.Profile("""{"dhanClientId":"1100","tokenValidity":"27/10/2026 17:30","activeSegment":"Equity","dataPlan":"Active"}""");

        read.Readable.ShouldBeTrue();
        read.Value!.TokenValidUntil.ShouldBe(Ist.At(27, 17, 30));
        DhanReads.Profile("""{"dhanClientId":"1100"}""").Value!.TokenValidUntil.ShouldBeNull();
        DhanReads.Profile("[]").Readable.ShouldBeFalse();
        DhanReads.Profile("not json").Readable.ShouldBeFalse();
    }

    [Fact]
    public void The_static_ip_answer_keeps_the_two_slots_and_their_dates_and_an_empty_slot_is_null()
    {
        var read = DhanReads.StaticIp("""{"primaryIP":"203.0.113.7","secondaryIP":"","modifyDatePrimary":"2026-10-01","modifyDateSecondary":""}""");

        read.Value.ShouldBe(new IpFacts("203.0.113.7", null, new DateOnly(2026, 10, 1), null));
        DhanReads.StaticIp("nope").Readable.ShouldBeFalse();
    }
}
