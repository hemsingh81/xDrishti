using System.ComponentModel.DataAnnotations;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Host;

/// <summary>The process environment (a setting of the host, never data, never changeable from the browser). The default is <c>none</c>.</summary>
public sealed class ExecOptions
{
    public const string Section = "Execution";

    [Required]
    public string Environment { get; set; } = "none";

    public ExecEnvironment Parsed => ExecEnvironmentParser.Parse(Environment);
}
