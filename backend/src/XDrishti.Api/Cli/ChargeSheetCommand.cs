using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.ChargeSheet;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd charge-sheet show|confirm|revoke</c> (spec P8-23, ADR 0127). <c>show</c> prints what stands; it only reads. <c>confirm</c> and <c>revoke</c> are the owner's act, which needs the owner's password typed again for that act: the terminal has no password check and the
/// actor of a verb is <c>cli:xd</c>, which is not an owner, so these two refuse and say where to do it (the Settings screen, tab Charge sheet). Nothing is written by this verb.
/// </summary>
internal static class ChargeSheetCommand
{
    public const string Usage = """
        usage:
          xd charge-sheet show [--sheet ID]
            prints whether the owner's confirmation of the charge sheet stands (not_confirmed, confirmed, stale, sheet_missing, revoked), whether confirming is switched on, the stored sheet and the rates hash a confirmation must carry, and the history
          xd charge-sheet confirm | revoke
            refused here: confirming or revoking is the owner's act and needs the owner's password for that act; do it on the Settings screen (tab Charge sheet)
        nothing is written by this verb
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count >= 1 && args[0] is "confirm" or "revoke")
        {
            await console.Error.WriteLineAsync($"refused: '{args[0]}' is the owner's act and needs the owner's password for that act; the terminal cannot check one. Use the Settings screen, tab Charge sheet.");
            return XdCommand.Refused;
        }

        string? sheet = null;
        if (args.Count == 0 || args[0] != "show" || args.Count is not (1 or 3) || (args.Count == 3 && args[1] != "--sheet"))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        if (args.Count == 3)
        {
            sheet = args[2];
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>>>().HandleAsync(new GetChargeSheetStatusQuery(sheet), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(Render(result.Value));
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

    /// <summary>The status as lines of text.</summary>
    internal static string Render(ChargeSheetStatusView view)
    {
        var text = new StringBuilder();
        text.Append("charge sheet confirmation: ").AppendLine(view.State);
        text.AppendLine(view.Text);
        text.Append("confirming is switched ").AppendLine(view.CapabilityOn ? "on" : "off (the default)");
        if (view.Sheet is { } sheet)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"sheet {sheet.Id} - {Clean(sheet.Label)}; source: {Clean(sheet.Source)}; checked: {Clean(sheet.Checked) ?? "not recorded"}; {sheet.Schedules} schedules from {sheet.FirstEffective:yyyy-MM-dd} to {sheet.LastEffective:yyyy-MM-dd}");
            text.AppendLine(CultureInfo.InvariantCulture, $"rates sha256 {sheet.RatesSha256}");
        }

        if (view.SheetNote is { } note)
        {
            text.AppendLine(Clean(note));
        }

        foreach (var row in view.History)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"#{row.Id} {row.At:yyyy-MM-dd HH:mm}Z {row.Kind}{(row.SheetId is null ? string.Empty : " " + row.SheetId)} by {Clean(row.Actor)}: {Clean(row.Reason)}");
        }

        text.AppendLine(view.Meaning);
        return text.ToString();
    }

    /// <summary>Text the owner typed or the sheet holds, with control characters replaced, so that it cannot write escape sequences to the terminal.</summary>
    private static string? Clean(string? text) => text is null ? null : string.Concat(text.Select(c => char.IsControl(c) ? '?' : c));
}
