using XDrishti.Domain.Execution.Confirm;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>
/// Spec P12-08 criteria 4, 5, 8, 9 and 10 as pure rules: the expiry boundary by the clock given (the database's), the drift limit both ways, the password rule table, and each state refusal with its own code in a fixed order. An unreadable
/// fact always refuses.
/// </summary>
public sealed class ConfirmPolicyTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly DateOnly Session = new(2026, 10, 27);
    private static readonly DateTimeOffset Expires = new(2026, 10, 27, 9, 47, 30, Ist);

    private static ConfirmState Ok(DateTimeOffset? now = null) =>
        new(now ?? Expires.AddSeconds(-30), Expires, Session, true, "semi_auto", false, "ready", Session, Expires.AddMinutes(-5), true);

    [Fact]
    public void A_tap_at_09_47_29_9_passes_and_at_09_47_30_000_is_expired()
    {
        ConfirmPolicy.StateRefusal(Ok(Expires.AddMilliseconds(-100))).ShouldBeNull();
        ConfirmPolicy.StateRefusal(Ok(Expires)).ShouldBe(ConfirmCodes.TicketExpired);
        ConfirmPolicy.IsExpired(Expires.AddTicks(-1), Expires).ShouldBeFalse();
        ConfirmPolicy.IsExpired(Expires, Expires).ShouldBeTrue();
    }

    [Theory]
    [InlineData(413.53, null)]
    [InlineData(413.54, ConfirmCodes.PriceMoved)]
    [InlineData(411.07, null)]
    [InlineData(411.06, ConfirmCodes.PriceMoved)]
    [InlineData(412.30, null)]
    public void The_drift_limit_of_0_30_pct_counts_a_fall_like_a_rise(double last, string? expected)
    {
        var now = Expires.AddSeconds(-30);

        ConfirmPolicy.PriceRefusal(412.30m, (decimal)last, now.AddSeconds(-1), now, ConfirmSettings.Default).ShouldBe(expected);
    }

    [Fact]
    public void The_drift_is_measured_against_the_reference_in_percent()
    {
        decimal.Round(ConfirmPolicy.DriftPct(412.30m, 413.53m), 3).ShouldBe(0.298m);
        decimal.Round(ConfirmPolicy.DriftPct(412.30m, 413.54m), 3).ShouldBe(0.301m);
        decimal.Round(ConfirmPolicy.DriftPct(412.30m, 411.07m), 3).ShouldBe(0.298m);
    }

    [Fact]
    public void No_last_price_a_stale_one_or_one_from_the_future_is_price_unavailable()
    {
        var now = Expires.AddSeconds(-30);

        ConfirmPolicy.PriceRefusal(412.30m, null, null, now, ConfirmSettings.Default).ShouldBe(ConfirmCodes.PriceUnavailable);
        ConfirmPolicy.PriceRefusal(412.30m, 412.30m, now.AddSeconds(-6), now, ConfirmSettings.Default).ShouldBe(ConfirmCodes.PriceUnavailable);
        ConfirmPolicy.PriceRefusal(412.30m, 412.30m, now.AddSeconds(5), now, ConfirmSettings.Default).ShouldBe(ConfirmCodes.PriceUnavailable);
        ConfirmPolicy.PriceRefusal(412.30m, 0m, now, now, ConfirmSettings.Default).ShouldBe(ConfirmCodes.PriceUnavailable);
        ConfirmPolicy.PriceRefusal(412.30m, 412.30m, now.AddSeconds(-5), now, ConfirmSettings.Default).ShouldBeNull();
    }

    [Theory]
    [InlineData("400000", false, true, PasswordRule.NotionalOverStepUp)]
    [InlineData("600000", false, false, "")]
    [InlineData("494820", false, true, PasswordRule.NotionalOverStepUp)]
    [InlineData("494821", false, false, "")]
    [InlineData(null, false, true, PasswordRule.CapitalUnavailable)]
    [InlineData("0", false, true, PasswordRule.CapitalUnavailable)]
    [InlineData("600000", true, true, PasswordRule.SoftChecks)]
    public void The_password_is_asked_at_10_pct_of_capital_for_a_soft_non_pass_or_without_capital(string? capital, bool soft, bool required, string reason)
    {
        var rule = ConfirmPolicy.Password(49_482.00m, capital is null ? null : decimal.Parse(capital, System.Globalization.CultureInfo.InvariantCulture), 10m, soft);

        rule.Required.ShouldBe(required);
        if (required)
        {
            rule.Reasons.ShouldContain(reason);
        }
        else
        {
            rule.Reasons.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Each_state_refusal_has_its_own_code_and_the_order_is_late_switch_mode_halt_readiness_unlock()
    {
        ConfirmPolicy.StateRefusal(Ok()).ShouldBeNull();
        ConfirmPolicy.StateRefusal(Ok() with { ExecutionEnabled = false }).ShouldBe(ConfirmCodes.Disabled);
        ConfirmPolicy.StateRefusal(Ok() with { ExecutionEnabled = null }).ShouldBe(ConfirmCodes.Disabled);
        ConfirmPolicy.StateRefusal(Ok() with { EffectiveMode = "assisted" }).ShouldBe(ConfirmCodes.ModeChanged);
        ConfirmPolicy.StateRefusal(Ok() with { EffectiveMode = null }).ShouldBe(ConfirmCodes.ModeChanged);
        ConfirmPolicy.StateRefusal(Ok() with { EffectiveMode = "auto_live" }).ShouldBeNull();
        ConfirmPolicy.StateRefusal(Ok() with { Halted = true }).ShouldBe(ConfirmCodes.Halted);
        ConfirmPolicy.StateRefusal(Ok() with { Halted = null }).ShouldBe(ConfirmCodes.Halted);
        ConfirmPolicy.StateRefusal(Ok() with { ReadinessVerdict = "not_ready" }).ShouldBe(ConfirmCodes.NotReady);
        ConfirmPolicy.StateRefusal(Ok() with { ReadinessVerdict = "unavailable" }).ShouldBe(ConfirmCodes.NotReady);
        ConfirmPolicy.StateRefusal(Ok() with { ReadinessAt = Expires.AddMinutes(-17) }).ShouldBe(ConfirmCodes.NotReady);
        ConfirmPolicy.StateRefusal(Ok() with { ReadinessSession = Session.AddDays(-1) }).ShouldBe(ConfirmCodes.NotReady);
        ConfirmPolicy.StateRefusal(Ok() with { Unlocked = false }).ShouldBe(ConfirmCodes.NotUnlocked);

        ConfirmPolicy.StateRefusal(Ok(Expires) with { ExecutionEnabled = false, Halted = true }).ShouldBe(ConfirmCodes.TicketExpired);
        ConfirmPolicy.StateRefusal(Ok() with { EffectiveMode = "paper", Halted = true, Unlocked = false }).ShouldBe(ConfirmCodes.ModeChanged);
        ConfirmPolicy.StateRefusal(Ok() with { Halted = true, ReadinessVerdict = null, Unlocked = false }).ShouldBe(ConfirmCodes.Halted);
    }

    [Fact]
    public void Only_price_and_soft_set_refusals_close_the_ticket()
    {
        ConfirmPolicy.ClosesTicket(ConfirmCodes.PriceMoved).ShouldBeTrue();
        ConfirmPolicy.ClosesTicket(ConfirmCodes.PriceUnavailable).ShouldBeTrue();
        ConfirmPolicy.ClosesTicket(ConfirmCodes.SoftSetChanged).ShouldBeTrue();
        foreach (var code in new[] { ConfirmCodes.TicketExpired, ConfirmCodes.Disabled, ConfirmCodes.ModeChanged, ConfirmCodes.Halted, ConfirmCodes.NotReady, ConfirmCodes.NotUnlocked, ConfirmCodes.TextChanged })
        {
            ConfirmPolicy.ClosesTicket(code).ShouldBeFalse(code);
        }
    }

    [Fact]
    public void A_soft_confirmation_is_bound_to_the_exact_set_in_any_order()
    {
        ConfirmPolicy.SameSoftSet(["adv_share", "fno_ban"], ["fno_ban", "adv_share"]).ShouldBeTrue();
        ConfirmPolicy.SameSoftSet([], []).ShouldBeTrue();
        ConfirmPolicy.SameSoftSet(["adv_share"], ["adv_share", "fno_ban"]).ShouldBeFalse();
        ConfirmPolicy.SameSoftSet(["adv_share"], null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("checked the news", true)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    [InlineData("line\nbreak", false)]
    public void A_reason_is_one_to_200_characters_of_plain_text(string? reason, bool ok)
    {
        ConfirmPolicy.ReasonOk(reason).ShouldBe(ok);
        ConfirmPolicy.ReasonOk(new string('a', 201)).ShouldBeFalse();
        ConfirmPolicy.ReasonOk(new string('a', 200)).ShouldBeTrue();
    }

    [Fact]
    public void A_day_unlock_lasts_until_15_30_india_time_of_its_date_and_only_for_that_date()
    {
        var until = DayUnlock.ExpiresAt(Session);

        until.ShouldBe(new DateTimeOffset(2026, 10, 27, 15, 30, 0, Ist));
        DayUnlock.IsValid(Session, until, Session, until.AddTicks(-1)).ShouldBeTrue();
        DayUnlock.IsValid(Session, until, Session, until).ShouldBeFalse();
        DayUnlock.IsValid(Session, until, Session.AddDays(1), until.AddHours(-1)).ShouldBeFalse();
    }

    [Fact]
    public void The_settings_keep_a_stored_value_only_inside_its_range()
    {
        ConfirmSettings.FromValues(null).ShouldBe(ConfirmSettings.Default);
        ConfirmSettings.FromValues(new Dictionary<string, string> { [ConfirmSettings.TtlKey] = "120", [ConfirmSettings.DriftKey] = "0.5", [ConfirmSettings.StepUpKey] = "25", [ConfirmSettings.FeedMaxAgeKey] = "10" })
            .ShouldBe(new ConfirmSettings(120, 0.5m, 25m, 10));
        ConfirmSettings.FromValues(new Dictionary<string, string> { [ConfirmSettings.TtlKey] = "14", [ConfirmSettings.DriftKey] = "1.01", [ConfirmSettings.StepUpKey] = "x", [ConfirmSettings.FeedMaxAgeKey] = "31" })
            .ShouldBe(ConfirmSettings.Default);
    }
}
