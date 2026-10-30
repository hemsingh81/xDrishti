
using System.Text.Json;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;

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
/// Answers every pending command once. Two kinds are known: <c>ping</c> (proves the plane works, changes nothing) and, from P12-05, <c>halt</c> (the owner's button: it can only record a halt, which makes the system stricter). Every other kind is refused
/// with <c>execution.command_unknown</c>. No kind reaches a broker. A halt whose record cannot be written is NOT answered, so it is tried again on the next poll until it lands.
/// </summary>
public sealed partial class ExecCommandProcessor(IExecCommandStore store, HaltService? halts = null, TimeProvider? timeProvider = null, ILogger<ExecCommandProcessor>? logger = null)
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<Guid, (int Failures, DateTimeOffset Next)> _failing = [];

    public const string Ping = "ping";
    public const string Halt = "halt";
    public const string UnknownCode = "execution.command_unknown";
    public const string HaltInvalidCode = "execution.halt_invalid";

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var applied = 0;
        foreach (var command in await store.PendingAsync(20, cancellationToken))
        {
            if (_failing.TryGetValue(command.Id, out var waiting) && _clock.GetUtcNow() < waiting.Next)
            {
                continue; // backing off after repeated failures; the halt is still tried again
            }

            (string Result, string Code)? answer;
            try
            {
                answer = command.Kind switch
                {
                    Ping => ("done", "execution.ok"),
                    Halt when halts is not null => await HaltAsync(command, cancellationToken).ConfigureAwait(false),
                    _ => ("refused", UnknownCode),
                };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The halt could not be recorded; no answer is written and it is tried again: at once the first time, then after 2, 4, 8 ... up to 30 seconds. Never given up.
                var failures = (_failing.TryGetValue(command.Id, out var seen) ? seen.Failures : 0) + 1;
                var delay = failures <= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, failures - 1)));
                _failing[command.Id] = (failures, _clock.GetUtcNow() + delay);
                if (logger is not null)
                {
                    LogHaltFailed(logger, e.GetType().Name, failures);
                }

                continue;
            }

            _failing.Remove(command.Id);

            var (result, code) = answer!.Value;
            if (await store.AnswerAsync(command.Id, result, code, cancellationToken))
            {
                applied++;
            }
        }

        return applied;
    }

    [LoggerMessage(EventId = 6663, Level = LogLevel.Warning, Message = "A halt command could not be recorded ({Kind}), failure {Failures}; trying again with backoff")]
    private static partial void LogHaltFailed(ILogger logger, string kind, int failures);

    private async Task<(string Result, string Code)> HaltAsync(ExecCommand command, CancellationToken cancellationToken)
    {
        if (!TryParseHalt(command, out var level, out var reason, out var kill))
        {
            return ("refused", HaltInvalidCode);
        }

        await halts!.RequestAsOwnerAsync(command.AccountId, level, reason, command.Actor, kill, cancellationToken).ConfigureAwait(false);
        return ("done", "execution.halt_started");
    }

    // Level entries, cancel or exit_all only (freeze is the reconciliation's); an owner actor; a reason; the scope in the payload agrees with the account column.
    private static bool TryParseHalt(ExecCommand command, out HaltLevel level, out string reason, out bool kill)
    {
        level = HaltLevel.Entries;
        reason = string.Empty;
        kill = false;
        try
        {
            using var doc = JsonDocument.Parse(command.Payload);
            var root = doc.RootElement;
            var scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
            var word = root.TryGetProperty("level", out var lv) ? lv.GetString() : null;
            reason = (root.TryGetProperty("reason", out var rs) ? rs.GetString() : null)?.Trim() ?? string.Empty;
            kill = root.TryGetProperty("brokerKillSwitch", out var ks) && ks.ValueKind == JsonValueKind.True;
            var parsed = word switch
            {
                "entries" => HaltLevel.Entries,
                "cancel" => HaltLevel.Cancel,
                "exit_all" => HaltLevel.ExitAll,
                _ => (HaltLevel?)null,
            };
            if (parsed is not { } found)
            {
                return false;
            }

            level = found;
            return command.Actor.StartsWith("owner:", StringComparison.Ordinal) && reason.Length is > 0 and <= 300 && (scope == "system" ? command.AccountId is null : scope == "account" && command.AccountId is not null);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
