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

    public ExecEnvironment Parsed => ExecEnvironmentParser.Parse(Environment);

    /// <summary>The strict reading of the switch: only the text <c>true</c> (any case) turns it on; blank, missing or anything else is off.</summary>
    public static bool IsSendEnabled(IConfiguration configuration) => bool.TryParse(configuration[$"{Section}:SendEnabled"], out var on) && on;
}
