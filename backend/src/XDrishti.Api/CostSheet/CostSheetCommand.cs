using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Api.CostSheets;

/// <summary>
/// The <c>cost-sheet</c> verb (spec P3-03f, F4, F5): <c>load</c>, <c>show</c> and <c>check</c> a charge sheet. A host step that reads files and calls handlers, on the precedent of the <c>simulate</c> verb (ADR 0041); no decision lives here. Exit
/// codes as the other verb: 0 done (a <c>check</c> with rows beyond the tolerance is still 0: it reports, it does not judge), 1 refused with nothing written, 2 the command line is wrong, 3 an unexpected failure or a concurrent write, 130 cancelled.
/// </summary>
internal static class CostSheetCommand
{
    /// <summary>The largest sheet file the verb reads, in bytes; a larger file is refused before it is read.</summary>
    public const int MaxFileBytes = 64 * 1024;

    /// <summary>The author written to the row of a sheet: a verb has no signed-in session.</summary>
    public const string Actor = "cli:cost-sheet";

    /// <summary>The most report files one <c>check</c> reads.</summary>
    public const int MaxReportFiles = 10;

    /// <summary>The most bytes of report files one <c>check</c> reads together.</summary>
    public const int MaxTotalReportBytes = 50 * 1024 * 1024;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var parsed = CostSheetOptions.Parse(args);
        if (parsed.Options is not { } options)
        {
            foreach (var error in parsed.Errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(CostSheetOptions.Usage);
            return SimulateCommand.UsageError;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CostSheetCommand).FullName!);
        try
        {
            await using var scope = services.CreateAsyncScope();
            return options.Action switch
            {
                CostSheetAction.Load => await LoadAsync(options, scope.ServiceProvider, console, logger, cancellationToken),
                CostSheetAction.Show => await ShowAsync(options, scope.ServiceProvider, console, logger, cancellationToken),
                _ => await CheckAsync(options, scope.ServiceProvider, console, logger, cancellationToken),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled; nothing was written");
            return SimulateCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return SimulateCommand.Failed;
        }
    }

    private static async Task<int> LoadAsync(CostSheetOptions options, IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var path = options.Files[0];
        var read = await ReadBoundedAsync(path, MaxFileBytes, "the file", cancellationToken);
        if (read.Problem is not null)
        {
            return await RefuseAsync(console, [read.Problem]);
        }

        var text = Encoding.UTF8.GetString(read.Bytes!);

        var handler = provider.GetRequiredService<ICommandHandler<LoadCostSheetCommand, LoadedCostSheet>>();
        var result = await handler.HandleAsync(new LoadCostSheetCommand(text, options.Replace, Actor), cancellationToken);
        if (result.IsFailure)
        {
            return await FailAsync(console, result.Error);
        }

        var loaded = result.Value;
        var status = Status(loaded.Status);
        CostSheetLog.Loaded(logger, loaded.Id, status, loaded.RatesSha256);
        var output = new StringBuilder();
        output.Append("sheet: ").Append(loaded.Id).Append('\n');
        output.Append("status: ").Append(status).Append('\n');
        output.Append("rates hash: ").Append(loaded.RatesSha256).Append('\n');
        output.Append("label: ").Append(loaded.Label).Append('\n');
        output.Append(loaded.IsChecked ? "checked\n" : "unchecked\n");
        await console.Out.WriteAsync(output.ToString());
        return SimulateCommand.Done;
    }

    private static async Task<int> ShowAsync(CostSheetOptions options, IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<IQueryHandler<ShowCostSheetQuery, Result<ShownCostSheet>>>();
        var result = await handler.HandleAsync(new ShowCostSheetQuery(options.SheetId!), cancellationToken);
        if (result.IsFailure)
        {
            return await FailAsync(console, result.Error);
        }

        var shown = result.Value;
        CostSheetLog.Shown(logger, shown.Id, shown.RatesSha256);
        await console.Out.WriteAsync(shown.CanonicalJson + "\nrates hash: " + shown.RatesSha256 + "\nlabel: " + shown.Label + "\n");
        return SimulateCommand.Done;
    }

    private static async Task<int> CheckAsync(CostSheetOptions options, IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        if (options.Files.Count > MaxReportFiles)
        {
            return await RefuseAsync(console, [$"at most {MaxReportFiles} report files are read at once; {options.Files.Count} were given. Nothing was read."]);
        }

        var files = new List<TradeFileContent>(options.Files.Count);
        long total = 0;
        foreach (var path in options.Files)
        {
            var read = await ReadBoundedAsync(path, TradeCsvLimits.MaxBytes, "a report file", cancellationToken);
            if (read.Problem is not null)
            {
                return await RefuseAsync(console, [read.Problem]);
            }

            total += read.Bytes!.Length;
            if (total > MaxTotalReportBytes)
            {
                return await RefuseAsync(console, [$"the report files together are more than {MaxTotalReportBytes / (1024 * 1024)} MB; the rest was not read."]);
            }

            files.Add(new TradeFileContent(read.Bytes));
        }

        var handler = provider.GetRequiredService<IQueryHandler<CheckCostSheetQuery, Result<CostCheckReport>>>();
        var result = await handler.HandleAsync(new CheckCostSheetQuery(options.SheetId!, files), cancellationToken);
        if (result.IsFailure)
        {
            return await FailAsync(console, result.Error);
        }

        var report = result.Value;
        var compared = report.Classes.Sum(c => c.Rows);
        var beyond = report.Classes.Sum(c => c.Beyond);
        CostSheetLog.Checked(logger, report.SheetId, report.RatesSha256, report.Files, compared, beyond);
        await console.Out.WriteAsync(CostCheckText.Text(report));
        return SimulateCommand.Done;
    }

    /// <summary>
    /// Reads a file of at most <paramref name="max"/> bytes. A path that is not a regular, non-empty file (a directory, a device, a FIFO, a socket or a /proc file all report length 0 or do not exist as files) is refused without being read; a symbolic
    /// link to a regular file is fine. The read itself stops at <paramref name="max"/> + 1 bytes, so a file that grew after the length was looked at, or one that lied about it, is refused too.
    /// </summary>
    internal static async Task<(byte[]? Bytes, string? Problem)> ReadBoundedAsync(string path, int max, string what, CancellationToken cancellationToken)
    {
        var name = SimulateOptions.Show(Path.GetFileName(path));
        long length;
        try
        {
            var info = new FileInfo(path);
            length = info.Exists ? info.Length : -1;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            length = -1;
        }

        if (length <= 0)
        {
            return (null, $"{what} cannot be read: '{name}' is not a regular, non-empty file.");
        }

        if (length > max)
        {
            return (null, $"{what} is {length} bytes; the most read is {max} bytes. It was not read.");
        }

        try
        {
            await using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.Asynchronous });
            var buffer = new byte[max + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (n == 0)
                {
                    break;
                }

                count += n;
            }

            return count > max
                ? (null, $"{what} grew past {max} bytes while it was read. It was not used.")
                : (buffer[..count], null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (null, $"{what} cannot be read: '{name}'.");
        }
    }

    private static string Status(LoadStatus status) => status switch
    {
        LoadStatus.Created => "created",
        LoadStatus.Replaced => "replaced",
        LoadStatus.DescriptionUpdated => "description updated (same rates)",
        _ => "unchanged",
    };

    private static async Task<int> FailAsync(SimulateConsole console, Error error)
    {
        var lines = error.Fields is { Count: > 0 } fields
            ? fields.Select(f => (string.IsNullOrEmpty(f.Field) ? "(sheet)" : f.Field) + ": " + (f.Code ?? error.Code) + ": " + f.Message).ToList()
            : [error.Code + ": " + error.Message];
        await RefuseAsync(console, lines);
        return error.Code == CostErrors.ConcurrentWriteCode ? SimulateCommand.Failed : SimulateCommand.Refused;
    }

    private static async Task<int> RefuseAsync(SimulateConsole console, List<string> problems)
    {
        foreach (var line in problems.Take(SimulateCommand.MaxProblems))
        {
            await console.Error.WriteLineAsync(line);
        }

        if (problems.Count > SimulateCommand.MaxProblems)
        {
            await console.Error.WriteLineAsync("and " + (problems.Count - SimulateCommand.MaxProblems).ToString(CultureInfo.InvariantCulture) + " more problems");
        }

        return SimulateCommand.Refused;
    }
}
