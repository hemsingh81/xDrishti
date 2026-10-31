namespace XDrishti.Execution.Persistence;

/// <summary>
/// Section <c>ExecHandover</c>: the key of the sealed token hand-over (ADR 0171), from the secret file <c>ExecHandover__Key</c> (the same file <c>xd-api</c> seals with; Keychain <c>xdrishti/exec/handover-key</c>). Read only by the live readiness token source.
/// A missing or short key is not a start failure here: every lookup is then <c>token_unavailable</c> (fail closed) and a warning is logged once.
/// </summary>
public sealed class ExecHandoverKeyOptions
{
    public const string Section = "ExecHandover";

    public string Key { get; set; } = string.Empty;
}
