using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The owner's charge sheets over the settings table (spec P3-03f, F3): the row <c>research.cost_sheet.&lt;id&gt;</c> holds the canonical JSON. A read is a projection without tracking; a change is only collected in the scoped context (the caller
/// saves it through the unit of work). The sheet is configuration, not a secret, and is never logged here. A <c>test-</c> sheet is synthetic and is neither stored nor found.
/// </summary>
internal sealed class CostSheetStore(XDrishtiDbContext db) : ICostSheetStore
{
    public async ValueTask<Result<CostSheet>> FindAsync(string sheetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheetId);
        if (!CostSheet.IsValidId(sheetId) || sheetId.StartsWith(CostSheet.TestPrefix, StringComparison.Ordinal))
        {
            return Result.Failure<CostSheet>(CostErrors.SheetNotFound(sheetId));
        }

        var key = AppSettingKeys.CostSheetPrefix + sheetId;
        var text = await db.AppSettings.AsNoTracking().Where(x => x.Id == key).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return Result.Failure<CostSheet>(CostErrors.SheetNotFound(sheetId));
        }

        var parsed = CostSheetJson.Parse(text);
        if (parsed.IsFailure)
        {
            return Result.Failure<CostSheet>(parsed.Error);
        }

        // The key is the id: a row whose text names another sheet is not this sheet.
        return parsed.Value.Id == sheetId
            ? parsed
            : Result.Failure<CostSheet>(CostErrors.SheetInvalid("id", CostErrors.InvalidValue, "The stored text names another sheet than its key."));
    }

    public async ValueTask SaveAsync(CostSheet sheet, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (sheet.IsTest)
        {
            throw new ArgumentException("A test sheet is synthetic and is never stored.", nameof(sheet));
        }

        var key = AppSettingKeys.CostSheetPrefix + sheet.Id;
        var value = sheet.ToCanonicalJson();
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (row is null)
        {
            db.AppSettings.Add(AppSetting.Create(key, value, actor, now));
        }
        else
        {
            row.Change(value, actor, now);
        }
    }
}
