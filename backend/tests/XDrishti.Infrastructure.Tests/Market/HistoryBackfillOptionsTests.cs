using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Infrastructure.Market.Eod;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>CF-04 Part B: every switch of the backfill defaults OFF or safe, the bounds are checked at start-up, and the run window is read as India times.</summary>
public sealed class HistoryBackfillOptionsTests
{
    private static List<ValidationResult> Validate(HistoryOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options.Backfill, new ValidationContext(options.Backfill), results, validateAllProperties: true);
        Validator.TryValidateObject(options.Probe, new ValidationContext(options.Probe), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_backfill_is_off_by_default_with_the_documented_defaults()
    {
        var options = new HistoryOptions();

        options.Backfill.Enabled.ShouldBeFalse();
        (options.Backfill.Years, options.Backfill.ChunkDays, options.Backfill.MaxRetries, options.Backfill.DiskHeadroomFactor, options.Backfill.BytesPerRowEstimate).ShouldBe((5, 90, 3, 2, 60));
        options.Backfill.RunWindow.ShouldBe("20:00-06:00");
        (options.Probe.Instruments, options.Probe.Sessions, options.Probe.MedianBps, options.Probe.MaxBps, options.Probe.MinWithinMaxPct).ShouldBe((20, 10, 5.0, 50.0, 99.0));
        Validate(options).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(6, 90)]
    [InlineData(5, 6)]
    [InlineData(5, 91)]
    public void Years_and_chunk_days_outside_their_range_are_refused(int years, int chunkDays)
    {
        var options = new HistoryOptions();
        options.Backfill.Years = years;
        options.Backfill.ChunkDays = chunkDays;

        Validate(options).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("20:00-06:00", true)]
    [InlineData("00:00-23:59", true)]
    [InlineData("8pm-6am", false)]
    [InlineData("24:00-06:00", false)]
    [InlineData("20:00", false)]
    public void The_run_window_is_two_india_times(string window, bool valid)
    {
        var options = new HistoryOptions();
        options.Backfill.RunWindow = window;

        (Validate(options).Count == 0).ShouldBe(valid);
    }

    [Fact]
    public void The_settings_adapter_reads_the_window_and_the_switch()
    {
        var options = new HistoryOptions();
        options.Backfill.Enabled = true;
        options.Backfill.RunWindow = "21:30-05:15";

        var settings = new BackfillSettings(Options.Create(options));

        settings.Enabled.ShouldBeTrue();
        (settings.RunWindowFrom, settings.RunWindowTo).ShouldBe((new TimeOnly(21, 30), new TimeOnly(5, 15)));
    }
}
