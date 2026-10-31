using System.ComponentModel.DataAnnotations;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Host;

/// <summary>The process environment (a setting of the host, never data, never changeable from the browser). The default is <c>none</c>.</summary>
public sealed class ExecOptions
{
    public const string Section = "Execution";

    [Required]
    public string Environment { get; set; } = "none";

    /// <summary>
    /// Whether the host composes the send protocol at all (default OFF; <c>Execution__SendEnabled</c>). Even when true it takes effect only in the sandbox environment; live is refused everywhere. Nothing in the host calls the sender in this slice.
    /// </summary>
    public bool SendEnabled { get; set; }

    /// <summary>
    /// Whether the readiness probe reads the broker in the live environment with the account's token from the sealed hand-over (ADR 0171 step 6; default OFF; <c>Execution__ReadinessLiveEnabled</c>). Takes effect only in live; it composes no sender and the order
    /// client keeps refusing live. Until the egress decision (ADR 0171 step 5) the guard table refuses every live read, so readiness stays unavailable even when on.
    /// </summary>
    public bool ReadinessLiveEnabled { get; set; }

    public ExecEnvironment Parsed => ExecEnvironmentParser.Parse(Environment);

    /// <summary>The strict reading of the switch: only the text <c>true</c> (any case) turns it on; blank, missing or anything else is off.</summary>
    public static bool IsSendEnabled(IConfiguration configuration) => bool.TryParse(configuration[$"{Section}:SendEnabled"], out var on) && on;

    /// <summary>The strict reading of <see cref="ReadinessLiveEnabled"/>, as <see cref="IsSendEnabled"/>.</summary>
    public static bool IsReadinessLiveEnabled(IConfiguration configuration) => bool.TryParse(configuration[$"{Section}:ReadinessLiveEnabled"], out var on) && on;
}
