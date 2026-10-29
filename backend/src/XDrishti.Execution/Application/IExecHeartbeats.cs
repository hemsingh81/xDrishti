namespace XDrishti.Execution.Application;

/// <summary>What the service says about itself in its row of <c>platform.service_heartbeats</c> (name <c>exec</c>). Facts only: no token, no client id, no order.</summary>
public sealed record ExecHeartbeat(string State, string Environment, string SettingsHash, int CommandsAnswered);

public interface IExecHeartbeats
{
    public const string ServiceName = "exec";

    Task BeatAsync(ExecHeartbeat beat, CancellationToken cancellationToken);
}
