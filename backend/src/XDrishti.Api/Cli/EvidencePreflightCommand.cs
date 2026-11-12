using System.Text;
using System.Text.Json;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Evidence;

namespace XDrishti.Api.Cli;

/// <summary><c>xd evidence preflight [--json]</c> (spec EV-01): prints which prerequisite of the first-evidence runbook is MET, NOT MET or UNAVAILABLE, with the reason and the fix. Read only; no decision lives here. Exit 0 whatever the states, 2 wrong line, 3 failure, 130 cancelled.</summary>
internal static class EvidencePreflightCommand
{
    public const string Usage = """
        usage:
          xd evidence preflight [--json]
            prints the 12 prerequisites of the first-evidence runbook (docs/engineering/first-evidence-runbook.md), each MET / NOT MET / UNAVAILABLE with the reason and the fix;
            a fact that cannot be read is UNAVAILABLE, never MET; --json prints the same items as an array of {item, state, reason, fix}
            nothing is written, nothing is sent and no setting is changed
        exit codes: 0 done (whatever the states), 2 wrong command line, 3 failure, 130 cancelled

        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0 || args[0] != "preflight" || args.Count > 2 || (args.Count == 2 && args[1] != "--json"))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var view = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>>().HandleAsync(new GetEvidencePreflightQuery(), cancellationToken);
            await console.Out.WriteAsync(args.Count == 2 ? Json(view) : Text(view));
            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }

    /// <summary>The items as text: one line each, the fix on the line below when the item is not met.</summary>
    internal static string Text(EvidencePreflightView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var text = new StringBuilder();
        foreach (var item in view.Items)
        {
            text.Append(item.State.PadRight(11)).Append(' ').Append(Clean(item.Item)).Append(": ").AppendLine(Clean(item.Reason));
            if (item.Fix != "none")
            {
                text.Append("            fix: ").AppendLine(Clean(item.Fix));
            }
        }

        return text.ToString();
    }

    /// <summary>The same items as an array of <c>{item, state, reason, fix}</c>.</summary>
    internal static string Json(EvidencePreflightView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return JsonSerializer.Serialize(view.Items, JsonOptions) + "\n";
    }

    private static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? '?' : c));
}
