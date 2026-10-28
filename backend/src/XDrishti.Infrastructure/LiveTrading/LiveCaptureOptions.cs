using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// <c>Live:Capture</c> (spec P11-01, ADR 0149): its own sub-section class, not on the <c>Live</c> root (the feed's keys there are not this class's). Every switch is OFF by default: <see cref="Enabled"/> seeds the two services paused or running at the
/// first start, <see cref="OrderBook"/> decides whether the order book is read at all.
/// </summary>
public sealed class LiveCaptureOptions
{
    public const string SectionName = "Live:Capture";

    /// <summary>The first-start seed of <c>live-capture</c> and <c>live-settle</c>; the owner switches them in the app afterwards.</summary>
    public bool Enabled { get; set; }

    public LiveCaptureOrderBookOptions OrderBook { get; set; } = new();

    [Range(60, 600)]
    public int PollSeconds { get; set; } = 120;
}

/// <summary><c>Live:Capture:OrderBook</c>: the order book is read only when <see cref="Enabled"/> (default false); without it every order-dependent field is unavailable.</summary>
public sealed class LiveCaptureOrderBookOptions
{
    public bool Enabled { get; set; }
}

/// <summary><c>Live:Match</c> (spec P11-01 decision 5): the tolerances of the flags. They flag, they never reject a match.</summary>
public sealed class LiveMatchOptions
{
    public const string SectionName = "Live:Match";

    [Range(0.01, 5.0)]
    public double ChasedEntryR { get; set; } = 0.25;

    [Range(0.01, 20.0)]
    public double ChasedEntryPct { get; set; } = 1.0;

    [Range(1.0, 100.0)]
    public double SizeBelowPct { get; set; } = 90;
}

/// <summary><c>Live:Protect</c> (spec P11-01 decision 6): how long after the last entry fill a stop may be placed and still count as protection.</summary>
public sealed class LiveProtectOptions
{
    public const string SectionName = "Live:Protect";

    [Range(60, 900)]
    public int WithinSeconds { get; set; } = 300;
}

/// <summary>The settings port over the three sections.</summary>
internal sealed class ConfiguredLiveCaptureSettings(IOptions<LiveCaptureOptions> capture, IOptions<LiveMatchOptions> match, IOptions<LiveProtectOptions> protect) : ILiveCaptureSettings
{
    public bool Enabled => capture.Value.Enabled;

    public bool OrderBookEnabled => capture.Value.OrderBook.Enabled;

    public int PollSeconds => capture.Value.PollSeconds;

    public MatchTolerances Tolerances => new((decimal)match.Value.ChasedEntryR, (decimal)match.Value.ChasedEntryPct, (decimal)match.Value.SizeBelowPct);

    public TimeSpan ProtectWithin => TimeSpan.FromSeconds(protect.Value.WithinSeconds);
}
