using System.Globalization;
using System.Text;
using XDrishti.Api.CostSheets;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// The <c>xd</c> verb (spec P5-07, ADR 0061): <c>strategy validate</c>, <c>strategy test</c> and <c>lab runs</c>. A host step that reads a file and calls the Lab's own handlers (the use cases the API calls), on the precedent of the other
/// verbs; no decision lives here. A test stores the version and queues the run; the running <c>xd-api</c> carries it out and the verb waits by reading the run. Exit codes: 0 done (a full test: PASS), 1 refused, 2 wrong command line,
/// 3 failure or timeout, 4 a full test with verdict MARGINAL or FAIL, 130 cancelled.
/// </summary>
internal static class XdCommand
{
    public const int Done = SimulateCommand.Done;

    public const int Refused = SimulateCommand.Refused;

    public const int UsageError = SimulateCommand.UsageError;

    public const int Failed = SimulateCommand.Failed;

    public const int NotPassing = 4;

    public const int Cancelled = SimulateCommand.Cancelled;

    /// <summary>The wait between two reads of a run.</summary>
    public static readonly TimeSpan DefaultPoll = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan QueuedHint = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, TimeSpan poll, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var parsed = XdOptions.Parse(args);
        if (parsed.Options is not { } options)
        {
            foreach (var error in parsed.Errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(XdOptions.Usage);
            return UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            return options.Action switch
            {
                XdAction.StrategyValidate => await ValidateAsync(options, provider, console, cancellationToken),
                XdAction.StrategyTest => await TestAsync(options, provider, console, poll, cancellationToken),
                _ => await RunsAsync(options, provider, console, cancellationToken),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return Failed;
        }
    }

    private static async Task<(string? Text, int? Exit)> ReadAsync(XdOptions options, SimulateConsole console, CancellationToken cancellationToken)
    {
        var read = await CostSheetCommand.ReadBoundedAsync(options.File!, LabErrors.MaxTextBytes, "the strategy file", cancellationToken);
        if (read.Problem is not null)
        {
            await console.Error.WriteLineAsync(read.Problem);
            return (null, Refused);
        }

        return (Encoding.UTF8.GetString(read.Bytes!), null);
    }

    private static async Task<int> ValidateAsync(XdOptions options, IServiceProvider provider, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (text, exit) = await ReadAsync(options, console, cancellationToken);
        if (text is null)
        {
            return exit!.Value;
        }

        var result = await provider.GetRequiredService<IQueryHandler<ValidateStrategyQuery, StrategyValidation>>().HandleAsync(new ValidateStrategyQuery(text), cancellationToken);
        await WriteIssuesAsync(console, options.File!, result.Problems);
        if (!result.Valid)
        {
            return Refused;
        }

        await console.Out.WriteLineAsync($"valid: {result.Id} version {result.Version?.ToString(CultureInfo.InvariantCulture) ?? "-"} hash {result.Hash}");
        return Done;
    }

    private static async Task<int> TestAsync(XdOptions options, IServiceProvider provider, SimulateConsole console, TimeSpan poll, CancellationToken cancellationToken)
    {
        var (text, exit) = await ReadAsync(options, console, cancellationToken);
        if (text is null)
        {
            return exit!.Value;
        }

        var created = await provider.GetRequiredService<ICommandHandler<CreateStrategyCommand, CreatedStrategy>>().HandleAsync(new CreateStrategyCommand(text, options.Note), cancellationToken);
        if (created.IsFailure)
        {
            if (created.Error.Payload is { } payload && payload.TryGetValue(LabErrors.ProblemsKey, out var problems) && problems is IReadOnlyList<StrategyIssue> issues)
            {
                await WriteIssuesAsync(console, options.File!, issues);
            }
            else
            {
                await console.Error.WriteLineAsync(created.Error.Code + ": " + created.Error.Message);
            }

            return Refused;
        }

        var strategy = created.Value;
        await console.Error.WriteLineAsync($"stored {strategy.Id} version {strategy.Version.ToString(CultureInfo.InvariantCulture)} ({(strategy.Created ? "new" : "already stored")})");

        var started = await provider.GetRequiredService<ICommandHandler<StartLabRunCommand, StartedLabRun>>().HandleAsync(
            new StartLabRunCommand(strategy.Id, strategy.Version, options.BasketId, options.Symbols.Count > 0 ? options.Symbols : null, options.Method, options.From, options.To),
            cancellationToken);
        if (started.IsFailure)
        {
            await console.Error.WriteLineAsync(started.Error.Code + ": " + started.Error.Message);
            return Refused;
        }

        var run = started.Value.Run;
        await console.Error.WriteLineAsync($"run {run.Id:D} {run.State}");
        return await WaitAsync(run, options, provider, console, poll, cancellationToken);
    }

    private static async Task<int> WaitAsync(LabRunView run, XdOptions options, IServiceProvider provider, SimulateConsole console, TimeSpan poll, CancellationToken cancellationToken)
    {
        var get = provider.GetRequiredService<IQueryHandler<GetLabRunQuery, Result<LabRunView>>>();
        var time = provider.GetRequiredService<TimeProvider>();
        var deadline = time.GetUtcNow().AddMinutes(options.TimeoutMinutes);
        var began = time.GetUtcNow();
        var hinted = false;
        var lastLine = string.Empty;
        try
        {
            while (true)
            {
                if (run.State is not ("queued" or "running"))
                {
                    break;
                }

                var line = $"{run.State} {run.Stage ?? "-"} {run.Progress.Done.ToString(CultureInfo.InvariantCulture)}/{run.Progress.Total.ToString(CultureInfo.InvariantCulture)}";
                if (line != lastLine)
                {
                    await console.Error.WriteLineAsync(line);
                    lastLine = line;
                }

                if (!hinted && run.State == "queued" && time.GetUtcNow() - began > QueuedHint)
                {
                    hinted = true;
                    await console.Error.WriteLineAsync("the run is still queued: xd-api carries runs out; is it running?");
                }

                if (time.GetUtcNow() >= deadline)
                {
                    await console.Error.WriteLineAsync($"timed out after {options.TimeoutMinutes.ToString(CultureInfo.InvariantCulture)} minutes; the run {run.Id:D} goes on (read it with 'xd lab runs')");
                    return Failed;
                }

                await Task.Delay(poll, time, cancellationToken);
                var again = await get.HandleAsync(new GetLabRunQuery(run.Id), cancellationToken);
                if (again.IsFailure)
                {
                    await console.Error.WriteLineAsync(again.Error.Code + ": " + again.Error.Message);
                    return Failed;
                }

                run = again.Value;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl-C: stop the run too, with a token that is not cancelled.
            await provider.GetRequiredService<ICommandHandler<CancelLabRunCommand, LabRunView>>().HandleAsync(new CancelLabRunCommand(run.Id), CancellationToken.None);
            await console.Error.WriteLineAsync($"cancelled; run {run.Id:D} was asked to stop");
            return Cancelled;
        }

        return await ReportAsync(run, console);
    }

    private static async Task<int> ReportAsync(LabRunView run, SimulateConsole console)
    {
        foreach (var p in run.Problems)
        {
            await console.Error.WriteLineAsync($"{p.Stage}: {Where(p.Path, p.Line, p.Column)}{p.Severity} {p.Code} {p.Message}");
        }

        await console.Out.WriteLineAsync($"run {run.Id:D} {run.State} key {run.Key}");
        foreach (var label in run.Labels)
        {
            await console.Out.WriteLineAsync("  " + label);
        }

        if (run.Counts is { } c)
        {
            await console.Out.WriteLineAsync($"  instruments {c.InstrumentsRun}/{c.Instruments} (skipped {c.Skipped}), trades {c.Trades}, open at end {c.OpenAtEnd}");
        }

        foreach (var key in run.SimRunKeys)
        {
            await console.Out.WriteLineAsync("  simulation run " + key);
        }

        if (run.Verdict is { } v)
        {
            await console.Out.WriteLineAsync($"  verdict {v.Outcome} (variants {v.VariantCount})");
            foreach (var g in v.Gates)
            {
                await console.Out.WriteLineAsync($"    {(g.Passed ? "pass" : "FAIL")} {g.Name}: {g.Reason}");
            }

            foreach (var limit in v.Limits)
            {
                await console.Out.WriteLineAsync("    limit: " + limit);
            }
        }

        if (run.State != "succeeded")
        {
            if (run.Error is not null)
            {
                await console.Error.WriteLineAsync("error: " + run.Error);
            }

            return run.State == "failed" && run.Problems.Count > 0 ? Refused : Failed;
        }

        return run.Verdict is { Outcome: not "PASS" } ? NotPassing : Done;
    }

    private static async Task<int> RunsAsync(XdOptions options, IServiceProvider provider, SimulateConsole console, CancellationToken cancellationToken)
    {
        var result = await provider.GetRequiredService<IQueryHandler<ListLabRunsQuery, Result<IReadOnlyList<LabRunView>>>>().HandleAsync(new ListLabRunsQuery(options.Strategy, options.State, options.Limit), cancellationToken);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(result.Error.Code + ": " + result.Error.Message);
            return Refused;
        }

        foreach (var r in result.Value)
        {
            var verdict = r.Verdict?.Outcome ?? "-";
            await console.Out.WriteLineAsync($"{r.Id:D} {r.CreatedAt:yyyy-MM-dd HH:mm}Z {r.State,-11} {r.Method,-5} {r.Strategy.Id} v{r.Strategy.Version.ToString(CultureInfo.InvariantCulture)} verdict {verdict}");
        }

        return Done;
    }

    private static async Task WriteIssuesAsync(SimulateConsole console, string file, IReadOnlyList<StrategyIssue> issues)
    {
        var name = SimulateOptions.Show(Path.GetFileName(file));
        foreach (var i in issues.Take(SimulateCommand.MaxProblems))
        {
            var line = $"{name}:{Where(i.Path, i.Line, i.Column)}{i.Severity} {i.Code} {SimulateOptions.Show(i.Message)}" + (i.Suggestion is null ? string.Empty : $" (try: {SimulateOptions.Show(i.Suggestion)})");
            await console.Error.WriteLineAsync(line);
        }
    }

    private static string Where(string path, int? line, int? column) =>
        (line is null ? string.Empty : $"{line.Value.ToString(CultureInfo.InvariantCulture)}:{(column ?? 1).ToString(CultureInfo.InvariantCulture)}: ") + (string.IsNullOrEmpty(path) ? string.Empty : $"[{SimulateOptions.Show(path)}] ");
}
