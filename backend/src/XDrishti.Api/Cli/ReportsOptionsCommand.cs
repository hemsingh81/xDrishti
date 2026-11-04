using System.Text.Json;
using XDrishti.Api.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionReports;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd reports options</c> (spec P14-16): prints the read-only options and stat-arb report by calling the use case, the same handlers the routes use. No decision lives here. Exit 0 whatever the data say.</summary>
internal static class ReportsOptionsCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = ReportsOptionsOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(ReportsOptionsOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetOptionsReportQuery, Result<OptionsReportView>>>()
                .HandleAsync(new GetOptionsReportQuery(options.Cut), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(options.Json ? JsonSerializer.Serialize(result.Value.Data, ApiJsonContext.Default.OptionsReportData) + "\n" : result.Value.Text);
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
}
