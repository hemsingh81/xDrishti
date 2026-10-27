using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Mcp;

/// <summary>
/// The one place where the tool contract is enforced (spec P9-02 AC 5 and 6): the assistant switch, argument validation, the timeout, the row cap, the cleaning and cutting of cells, the size limits, the envelope and the log event. A tool body only builds a <see cref="ToolOutcome"/>; it
/// cannot skip a rule. The switch is read from the database on every call; when it is off nothing else is queried.
/// </summary>
internal sealed class ToolRunner(IAiContext context, IOptions<McpOptions> options, TimeProvider time, ILogger<ToolRunner> logger)
{
    public async Task<string> RunAsync(ToolSpec spec, IEnumerable<KeyValuePair<string, object?>>? rawArguments, Func<ToolArguments, CancellationToken, Task<ToolOutcome>> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(body);
        var clock = Stopwatch.StartNew();
        var asOf = time.GetUtcNow();
        var materialised = rawArguments?.ToList();
        var argumentHash = HashOf(materialised);
        ToolEnvelope Finish(ToolEnvelope envelope, string status)
        {
            logger.Call(spec.Name, argumentHash, envelope.RowCount, clock.ElapsedMilliseconds, status);
            if (envelope.Redactions > 0 || envelope.InjectionFlags > 0)
            {
                logger.Flags(spec.Name, envelope.Redactions, envelope.InjectionFlags);
            }

            return envelope;
        }

        ToolEnvelope Refuse(string code, string message)
        {
            logger.Refused(spec.Name, code);
            return Finish(ToolEnvelope.Failure(spec.Name, asOf, code, message), "refused");
        }

        ToolEnvelope envelope;
        try
        {
            var state = await context.GetAsync(cancellationToken);
            if (!state.AssistantEnabled)
            {
                envelope = Refuse(ToolErrorCodes.Disabled, "The assistant is switched off.");
                return envelope.Fit().Json;
            }

            var (arguments, problem) = spec.Validate(materialised);
            if (arguments is null)
            {
                envelope = Refuse(ToolErrorCodes.InvalidArguments, problem!);
                return envelope.Fit().Json;
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(options.Value.ToolTimeoutSeconds));
            ToolOutcome outcome;
            try
            {
                outcome = await body(arguments, limit.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                envelope = Refuse(ToolErrorCodes.Timeout, $"The tool took longer than {options.Value.ToolTimeoutSeconds} seconds.");
                return envelope.Fit().Json;
            }

            if (outcome.RejectedReason is not null)
            {
                logger.SqlRejected(outcome.RejectedReason);
            }

            var shaped = Shape(spec.Name, asOf, outcome);
            var (fitted, json) = shaped.Fit();
            Finish(fitted, fitted.Ok ? "ok" : fitted.Error!.Code);
            return json;
        }
        catch (AiDatabaseUnavailableException)
        {
            envelope = Refuse(ToolErrorCodes.Unavailable, "The read-only database is not available.");
            return envelope.Fit().Json;
        }
#pragma warning disable CA1031 // a tool never ends in an exception the SDK would turn into an unstructured error: any other failure is one fixed answer (the exception type is not even logged; its message could hold a value)
        catch (Exception failure) when (failure is not OperationCanceledException)
#pragma warning restore CA1031
        {
            envelope = Refuse(ToolErrorCodes.Failed, "The tool failed; try a simpler call.");
            return envelope.Fit().Json;
        }
    }

    /// <summary>Applies the row cap and the cleaning of the cells; the size fit comes after.</summary>
    internal static ToolEnvelope Shape(string tool, DateTimeOffset asOf, ToolOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            return new ToolEnvelope(false, tool, asOf, [], null, outcome.Data, null, false, outcome.Evidence, outcome.Notes, outcome.Error);
        }

        var counters = new CleanCounters();
        IReadOnlyList<IReadOnlyList<object?>>? rows = null;
        var truncated = outcome.Truncated;
        var total = outcome.TotalRows;
        if (outcome.Rows is { } source)
        {
            if (source.Count > ToolLimits.MaxRows)
            {
                truncated = true;
                total ??= source.Count;
            }

            rows =
            [
                .. source.Take(ToolLimits.MaxRows).Select(row =>
                    (IReadOnlyList<object?>)[.. row.Select((cell, i) => CellCleaner.Clean(cell, i < outcome.Columns.Count && outcome.Columns[i].IsText, counters))]),
            ];
        }

        return new ToolEnvelope(true, tool, asOf, [.. outcome.Columns.Select(c => c.Name)], rows, CleanData(outcome.Data, counters), total, truncated, outcome.Evidence, outcome.Notes, null, counters.Redactions, counters.InjectionFlags, outcome.OmitNullCells);
    }

    /// <summary>
    /// Free text inside a tool's data object: a property whose name ends in <c>Text</c> (<c>basisText</c>, <c>nameText</c>) holds text that came from the database and is cleaned and scrubbed like a text cell, and counted. Every other string of the data is written by the system.
    /// </summary>
    private static JsonObject? CleanData(JsonObject? data, CleanCounters counters)
    {
        if (data is null)
        {
            return null;
        }

        var copy = (JsonObject)data.DeepClone();
        foreach (var property in copy.ToList())
        {
            if (property.Key.EndsWith("Text", StringComparison.Ordinal) && property.Value is JsonValue value && value.TryGetValue<string>(out var text))
            {
                copy[property.Key] = (string?)CellCleaner.Clean(text, true, counters);
            }
        }

        return copy;
    }

    /// <summary>A short hash of the arguments for the log line: enough to tell calls apart, nothing to read back.</summary>
    private static string HashOf(List<KeyValuePair<string, object?>>? arguments)
    {
        var text = arguments is null ? string.Empty : string.Join('|', arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + (a.Value is JsonElement e ? e.GetRawText() : a.Value?.ToString())));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
