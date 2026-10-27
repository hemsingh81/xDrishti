using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Ai.Evaluation;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd eval</c> (spec P9-09 Contract, ADR 0143): the owner's verb for the assistant eval. It runs the eval once (never started by anything else), grades answers from a file, compares, accepts a baseline (actor <c>cli:xd</c>), prints a report, and seeds the fixture database for the developer script.
/// A host step that calls the use cases; no decision lives here. It writes no setting and confirms nothing.
/// </summary>
internal static class EvalCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = EvalCliOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(EvalCliOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            return options.Verb switch
            {
                "run" => await RunEvalAsync(options, sp, console, cancellationToken),
                "grade" => await GradeAsync(options, sp, console, cancellationToken),
                "compare" => await CompareAsync(options, sp, console, cancellationToken),
                "accept" => await AcceptAsync(options, sp, console, cancellationToken),
                "show" => await ShowAsync(options, sp, console, cancellationToken),
                _ => await SeedAsync(sp, console, cancellationToken),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (SeedRefusedException refused)
        {
            await console.Error.WriteLineAsync("refused: " + SimulateOptions.Show(refused.Message));
            return XdCommand.Refused;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }

    private static async Task<int> RunEvalAsync(EvalCliOptions o, IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        var result = await sp.GetRequiredService<ICommandHandler<RunEvalCommand, EvalRunView>>().HandleAsync(new RunEvalCommand(o.Sets, o.Repeats, o.Target, EvalErrors.CliActor), ct);
        return await PrintRunAsync(result, console);
    }

    private static async Task<int> GradeAsync(EvalCliOptions o, IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(o.AnswersFile!, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await console.Error.WriteLineAsync("the answers file cannot be read");
            return XdCommand.UsageError;
        }

        var result = await sp.GetRequiredService<ICommandHandler<GradeEvalAnswersCommand, EvalRunView>>().HandleAsync(new GradeEvalAnswersCommand(text, o.Target, EvalErrors.CliActor), ct);
        return await PrintRunAsync(result, console);
    }

    private static async Task<int> PrintRunAsync(Result<EvalRunView> result, SimulateConsole console)
    {
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        var run = result.Value;
        await console.Out.WriteLineAsync($"run {run.Id}: status {run.Status}, verdict {run.Verdict}{(run.Gating ? string.Empty : " (non-gating: it can never be accepted)")}");
        foreach (var set in run.Summary.Sets)
        {
            await console.Out.WriteLineAsync($"{set.Set}: {set.Outcome.State}");
            foreach (var rate in set.Rates)
            {
                await console.Out.WriteLineAsync($"  {rate.Name}: {rate.K} of {rate.N} (95 % interval {rate.Low.ToString("0.000", CultureInfo.InvariantCulture)} to {rate.High.ToString("0.000", CultureInfo.InvariantCulture)})");
            }

            foreach (var reason in set.Outcome.Reasons)
            {
                await console.Out.WriteLineAsync("  - " + SimulateOptions.Show(reason));
            }
        }

        await console.Out.WriteLineAsync(run.Summary.SmallSampleLine);
        await console.Out.WriteLineAsync($"report: xd eval show {run.Id} [--json|--md|--answers]; accept: xd eval accept {run.Id}");
        return run.Verdict == EvalVerdict.Pass && run.Gating ? XdCommand.Done : run.Status != "completed" ? XdCommand.Refused : XdCommand.NotPassing;
    }

    private static async Task<int> CompareAsync(EvalCliOptions o, IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        var result = await sp.GetRequiredService<IQueryHandler<CompareEvalsQuery, Result<EvalComparisonView>>>().HandleAsync(new CompareEvalsQuery(o.Run!.Value, o.Baseline), ct);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        var view = result.Value;
        await console.Out.WriteLineAsync($"run {view.RunId} against {view.BaselineId}{(view.Differences.Count == 0 ? string.Empty : "; differs in: " + string.Join(", ", view.Differences))}");
        foreach (var set in view.Sets)
        {
            await console.Out.WriteLineAsync($"{set.Set}: {set.PassesNow} of {set.Cases} now, {set.PassesBefore} before; discordant pairs b {set.Paired.B} (passed, now fails) c {set.Paired.C} (the reverse); sign-test p {set.Paired.PValue.ToString("0.0000", CultureInfo.InvariantCulture)}{(set.Regression ? "; REGRESSION" : string.Empty)}");
            await console.Out.WriteLineAsync("  " + set.Sentence);
        }

        await console.Out.WriteLineAsync("(a comparison never blocks by itself: the gate is the thresholds)");
        return XdCommand.Done;
    }

    private static async Task<int> AcceptAsync(EvalCliOptions o, IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        var result = await sp.GetRequiredService<ICommandHandler<AcceptEvalCommand, EvalRunView>>().HandleAsync(new AcceptEvalCommand(o.Run!.Value, o.Reason, EvalErrors.CliActor), ct);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        await console.Out.WriteLineAsync($"accepted: run {result.Value.Id} is the baseline (fingerprint {result.Value.Fingerprint[..12]})");
        return XdCommand.Done;
    }

    private static async Task<int> ShowAsync(EvalCliOptions o, IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        var result = await sp.GetRequiredService<IQueryHandler<GetEvalQuery, Result<EvalDetailView>>>().HandleAsync(new GetEvalQuery(o.Run!.Value), ct);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        await console.Out.WriteAsync(o.Format switch { "json" => EvalReports.Json(result.Value), "answers" => EvalReports.Answers(result.Value), _ => EvalReports.Markdown(result.Value) });
        return XdCommand.Done;
    }

    /// <summary>Seeds the fixture into the database named by <c>Eval__SeedConnection</c> (an owner login of <c>xdrishti_eval</c>, from the environment of the developer script; never a setting file). Refuses any other database and any database that is not empty.</summary>
    private static async Task<int> SeedAsync(IServiceProvider sp, SimulateConsole console, CancellationToken ct)
    {
        var connection = Environment.GetEnvironmentVariable("Eval__SeedConnection");
        if (string.IsNullOrWhiteSpace(connection))
        {
            await console.Error.WriteLineAsync("Eval__SeedConnection is not set (scripts/eval-db.sh sets it)");
            return XdCommand.UsageError;
        }

        var files = await sp.GetRequiredService<XDrishti.Application.Abstractions.Ai.IEvalFiles>().ReadAsync(ct);
        if (files.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(files.Error.Code + ": " + files.Error.Message));
            return XdCommand.Refused;
        }

        var fixture = EvalFixtureReader.Parse(files.Value.Fixture);
        if (fixture.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(fixture.Error.Message));
            return XdCommand.Refused;
        }

        // The owner's password is a secret file of the container (Database:Password), never part of the connection text.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        if (string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Database:Password"];
        }

        var outcome = await FixtureSeeder.SeedAsync(builder.ConnectionString, fixture.Value, files.Value.Fixture, "xdrishti_eval", ct);
        await console.Out.WriteLineAsync(outcome == SeedOutcome.Seeded ? "seeded: the fixture is in xdrishti_eval" : "unchanged: xdrishti_eval already holds this fixture");
        return XdCommand.Done;
    }
}
