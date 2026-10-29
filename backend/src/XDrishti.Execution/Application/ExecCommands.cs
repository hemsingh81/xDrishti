
namespace XDrishti.Execution.Application;

/// <summary>One instruction from <c>xd-api</c>, left as a row in <c>execution.commands</c>. The payload is text of at most 2 KB and is never logged.</summary>
public sealed record ExecCommand(Guid Id, string Kind, Guid? AccountId, string Payload, string Actor, DateTimeOffset At);

public interface IExecCommandStore
{
    /// <summary>The oldest commands that have no answer yet.</summary>
    Task<IReadOnlyList<ExecCommand>> PendingAsync(int max, CancellationToken cancellationToken);

    /// <summary>Writes the one answer of a command. False when it was answered before (an id is never applied twice).</summary>
    Task<bool> AnswerAsync(Guid commandId, string result, string code, CancellationToken cancellationToken);
}

/// <summary>
/// Answers every pending command once. In P12-01b the only known kind is <c>ping</c> (it proves the plane works and changes nothing); every other kind is refused with <c>execution.command_unknown</c>. Later slices add kinds here, each with its own
/// gate; no kind in this slice reaches a broker.
/// </summary>
public sealed class ExecCommandProcessor(IExecCommandStore store)
{
    public const string Ping = "ping";
    public const string UnknownCode = "execution.command_unknown";

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var applied = 0;
        foreach (var command in await store.PendingAsync(20, cancellationToken))
        {
            var (result, code) = command.Kind == Ping ? ("done", "execution.ok") : ("refused", UnknownCode);
            if (await store.AnswerAsync(command.Id, result, code, cancellationToken))
            {
                applied++;
            }
        }

        return applied;
    }
}
