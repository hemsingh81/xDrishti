namespace XDrishti.Execution.Host;

/// <summary>The boot state is always <c>recovering</c> in this slice: entries are blocked until recovery exists (P12-06). The heartbeat service reports it.</summary>
internal static class ExecBootService
{
    public const string BootState = "recovering";
}
