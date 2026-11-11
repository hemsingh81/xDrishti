using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Market.History;

namespace XDrishti.Infrastructure.Market.Eod;

internal sealed class BackfillSettings(IOptions<HistoryOptions> options) : IBackfillSettings
{
    public bool Enabled => options.Value.Backfill.Enabled;

    public int Years => options.Value.Backfill.Years;

    public int ChunkDays => options.Value.Backfill.ChunkDays;

    public int MaxRetries => options.Value.Backfill.MaxRetries;

    public int DiskHeadroomFactor => options.Value.Backfill.DiskHeadroomFactor;

    public int BytesPerRowEstimate => options.Value.Backfill.BytesPerRowEstimate;

    public TimeOnly RunWindowFrom => Window(0);

    public TimeOnly RunWindowTo => Window(1);

    public int ProbeInstruments => options.Value.Probe.Instruments;

    public int ProbeSessions => options.Value.Probe.Sessions;

    public double ProbeMedianBps => options.Value.Probe.MedianBps;

    public double ProbeMaxBps => options.Value.Probe.MaxBps;

    public double ProbeMinWithinMaxPct => options.Value.Probe.MinWithinMaxPct;

    // "HH:mm-HH:mm", validated at start-up.
    private TimeOnly Window(int part) => TimeOnly.ParseExact(options.Value.Backfill.RunWindow.Split('-')[part], "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
