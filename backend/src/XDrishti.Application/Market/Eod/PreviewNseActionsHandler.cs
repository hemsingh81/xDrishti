using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary>Turns the text of NSE's corporate-actions file into a PREVIEW of the existing import (P1-10a). Never applies anything: the owner imports and confirms in the app.</summary>
public sealed record PreviewNseActionsCommand(string Text) : ICommand;

/// <summary>The file's counts and the import preview (importable, duplicates, unknown symbols, <c>adjustsCsvTwice</c> warnings).</summary>
public sealed record NseActionsPreview(int Splits, int Bonuses, int Other, int Unreadable, ImportCorporateActionsResult Preview);

internal sealed class PreviewNseActionsHandler(ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult> import) : ICommandHandler<PreviewNseActionsCommand, NseActionsPreview>
{
    public async ValueTask<Result<NseActionsPreview>> HandleAsync(PreviewNseActionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var parsed = await NseActionsParser.ParseAsync(command.Text, cancellationToken);
        if (parsed.IsFailure)
        {
            return Result.Failure<NseActionsPreview>(parsed.Error);
        }

        var text = parsed.Value;
        if (text.Splits + text.Bonuses == 0)
        {
            return Result.Success(new NseActionsPreview(0, 0, text.Other, text.Unreadable, new ImportCorporateActionsResult(true, 0, 0, 0, 0, 0, [], [])));
        }

        // Preview: true is fixed here. The import path itself refuses to write on a preview, so nothing becomes applied.
        var preview = await import.HandleAsync(new ImportCorporateActionsCommand(text.Csv, Preview: true, "xd-cli:history"), cancellationToken);
        return preview.IsFailure
            ? Result.Failure<NseActionsPreview>(preview.Error)
            : Result.Success(new NseActionsPreview(text.Splits, text.Bonuses, text.Other, text.Unreadable, preview.Value));
    }
}
