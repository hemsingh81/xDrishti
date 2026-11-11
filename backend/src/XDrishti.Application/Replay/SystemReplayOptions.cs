using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Replay;

/// <summary>
/// The configuration section <c>Replay</c> (SR-01, AU-08 Internal): the bounds of the system replay. A value out of range fails start-up.
/// </summary>
public sealed class SystemReplayOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Replay";

    /// <summary>
    /// The share of the universe whose bars of a session may be incomplete before the session is <c>data_missing</c> (no new entries; open positions still exit). More than this share is data missing; exactly this share is not. Default 0.2.
    /// </summary>
    [Range(0.0, 1.0)]
    public double IncompleteDataShare { get; set; } = 0.2;
}
