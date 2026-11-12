namespace XDrishti.Application.Abstractions.Evidence;

/// <summary>
/// The facts of the host that no stored record holds (EV-01): what this build compiled in, the memory limit in <c>deploy/compose.yaml</c> and the <c>Data:History:Nse:Enabled</c> switch of this host's configuration. Read only; a fact that cannot be read is null, or the read throws:
/// the preflight says <c>UNAVAILABLE</c> for both and never <c>MET</c>.
/// </summary>
public interface IEvidencePreflightHost
{
    /// <summary>The newest migration compiled into this build; null when none is.</summary>
    string? NewestCompiledMigration();

    /// <summary>The <c>mem_limit</c> of the <c>xd-api</c> service in <c>deploy/compose.yaml</c>, in MiB; null when the file is missing or the value cannot be parsed.</summary>
    long? ComposeApiMemLimitMib();

    /// <summary>The <c>deploy/compose.yaml</c> that was read (its full path); null when none was found.</summary>
    string? ComposePath();

    /// <summary><c>Data:History:Nse:Enabled</c> as this host's configuration binds it.</summary>
    bool NseHistoryEnabled();
}
