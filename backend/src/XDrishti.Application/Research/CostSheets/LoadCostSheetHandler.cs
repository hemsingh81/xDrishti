using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>
/// Stores a charge sheet (spec P3-03f, F4). The rule that protects old runs: other rates under an existing id need an explicit replace (a run embeds its own copy, so an overwritten row never changes an old record, but the owner should mean it).
/// No audit event is written: the audit trail is the authentication trail, and the row's author and time say who loaded what.
/// </summary>
internal sealed class LoadCostSheetHandler(ICostSheetStore store, IPlatformUnitOfWork unitOfWork, TimeProvider time) : ICommandHandler<LoadCostSheetCommand, LoadedCostSheet>
{
    public async ValueTask<Result<LoadedCostSheet>> HandleAsync(LoadCostSheetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var parsed = CostSheetJson.Parse(command.Text);
        if (parsed.IsFailure)
        {
            return Result.Failure<LoadedCostSheet>(parsed.Error);
        }

        var sheet = parsed.Value;
        if (sheet.IsTest)
        {
            return Result.Failure<LoadedCostSheet>(Error.Validation(CostErrors.TestSheetNotStoredCode, $"The id '{sheet.Id}' starts with '{CostSheet.TestPrefix}': a test sheet is synthetic and is never stored."));
        }

        var existing = await store.FindAsync(sheet.Id, cancellationToken);
        LoadStatus status;
        string? previous = null;
        if (existing.IsSuccess)
        {
            previous = existing.Value.RatesSha256;
            if (previous != sheet.RatesSha256)
            {
                if (!command.Replace)
                {
                    return Result.Failure<LoadedCostSheet>(ReplaceRequired(sheet.Id, previous, sheet.RatesSha256));
                }

                status = LoadStatus.Replaced;
            }
            else if (existing.Value.Equals(sheet))
            {
                return Result.Success(Of(sheet, LoadStatus.Unchanged, previous));
            }
            else
            {
                status = LoadStatus.DescriptionUpdated;
            }
        }
        else if (existing.Error.Kind == ErrorKind.NotFound)
        {
            status = LoadStatus.Created;
        }
        else if (command.Replace)
        {
            // the stored text no longer parses: only an explicit replace may overwrite it
            status = LoadStatus.Replaced;
        }
        else
        {
            return Result.Failure<LoadedCostSheet>(ReplaceRequired(sheet.Id, "(the stored text does not parse)", sheet.RatesSha256));
        }

        await store.SaveAsync(sheet, command.Actor, time.GetUtcNow(), cancellationToken);
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<LoadedCostSheet>(Error.Conflict(CostErrors.ConcurrentWriteCode, $"Another write to the sheet '{sheet.Id}' landed first; nothing was written. Run the command again."));
        }

        return Result.Success(Of(sheet, status, previous));
    }

    private static LoadedCostSheet Of(CostSheet sheet, LoadStatus status, string? previous) => new(sheet.Id, sheet.RatesSha256, sheet.Label, sheet.Checked is not null, status, previous);

    private static Error ReplaceRequired(string id, string oldHash, string newHash) =>
        Error.Conflict(CostErrors.ReplaceRequiredCode, $"The sheet '{id}' is stored with other rates (stored {oldHash}, file {newHash}). Use a new id for a new set of rates, or --replace to overwrite this one.");
}
