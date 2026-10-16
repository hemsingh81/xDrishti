using System.ComponentModel.DataAnnotations;

namespace XDrishti.Feed.Tests;

/// <summary>The bounds of <c>Live:*</c> (P2-10 contract): a value outside them stops the feed at start.</summary>
public sealed class LiveOptionsTests
{
    private static List<ValidationResult> Check(LiveOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_defaults_are_valid_and_are_the_documented_ones()
    {
        var options = new LiveOptions();

        Check(options).ShouldBeEmpty();
        options.MaxWsInstruments.ShouldBe(200);
        options.FlushSeconds.ShouldBe(2);
        options.SilenceSeconds.ShouldBe(30);
        options.BackoffInitialSeconds.ShouldBe(1);
        options.BackoffMaxSeconds.ShouldBe(30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public void The_cap_of_the_active_set_is_one_to_five_thousand(int value) =>
        Check(new LiveOptions { MaxWsInstruments = value }).ShouldNotBeEmpty();

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public void The_limits_of_the_cap_are_valid(int value) =>
        Check(new LiveOptions { MaxWsInstruments = value }).ShouldBeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void The_flush_interval_is_one_to_thirty_seconds(int value) =>
        Check(new LiveOptions { FlushSeconds = value }).ShouldNotBeEmpty();

    [Theory]
    [InlineData(4)]
    [InlineData(301)]
    public void The_silence_limit_is_five_to_three_hundred_seconds(int value) =>
        Check(new LiveOptions { SilenceSeconds = value }).ShouldNotBeEmpty();

    [Fact]
    public void The_back_off_maximum_must_not_be_below_its_start()
    {
        Check(new LiveOptions { BackoffInitialSeconds = 20, BackoffMaxSeconds = 10 }).ShouldNotBeEmpty();
        Check(new LiveOptions { BackoffInitialSeconds = 10, BackoffMaxSeconds = 10 }).ShouldBeEmpty();
    }

    [Fact]
    public void The_options_become_the_settings_of_the_feed_logic()
    {
        var settings = new LiveOptions { MaxWsInstruments = 50, FlushSeconds = 3, SilenceSeconds = 40, BackoffInitialSeconds = 2, BackoffMaxSeconds = 60, SubscribeSpacingMilliseconds = 100 }.ToSettings();

        settings.MaxInstruments.ShouldBe(50);
        settings.FlushInterval.ShouldBe(TimeSpan.FromSeconds(3));
        settings.Silence.ShouldBe(TimeSpan.FromSeconds(40));
        settings.BackoffInitial.ShouldBe(TimeSpan.FromSeconds(2));
        settings.BackoffMax.ShouldBe(TimeSpan.FromSeconds(60));
        settings.SubscribeSpacing.ShouldBe(TimeSpan.FromMilliseconds(100));
    }
}
