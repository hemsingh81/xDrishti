namespace XDrishti.Domain.Platform.Services;

/// <summary>The wire and storage codes of <see cref="ServiceRunTrigger"/> and <see cref="ServiceRunStatus"/>.</summary>
public static class ServiceRunCodes
{
    public static string ToCode(this ServiceRunTrigger trigger) => trigger switch
    {
        ServiceRunTrigger.CatchUp => "catch_up",
        _ => trigger.ToString().ToLowerInvariant(),
    };

    public static ServiceRunTrigger ParseTrigger(string code) => code switch
    {
        "catch_up" => ServiceRunTrigger.CatchUp,
        _ => Enum.Parse<ServiceRunTrigger>(code, ignoreCase: true),
    };

    public static string ToCode(this ServiceRunStatus status) => status.ToString().ToLowerInvariant();
}
