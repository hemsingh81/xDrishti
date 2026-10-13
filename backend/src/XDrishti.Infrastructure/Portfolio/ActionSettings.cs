using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The thresholds of the Action-center rules (ADR 0022): one <see cref="AppSetting"/> row holds them as a small JSON object (camel-case names of <see cref="ActionThresholds"/>); without the row, or with a row that cannot be read or is out of range
/// (edited by hand), the documented defaults apply. Reads are projections without tracking; a change is only collected in the scoped context (the use case saves it together with its audit event).
/// </summary>
internal sealed class ActionSettings(XDrishtiDbContext db) : IActionSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<ActionThresholdSetting> GetAsync(CancellationToken cancellationToken)
    {
        var row = await db.AppSettings.AsNoTracking()
            .Where(x => x.Id == AppSettingKeys.PortfolioActionThresholds)
            .Select(x => new { x.Value, x.UpdatedBy, x.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        return row is not null && Read(row.Value) is { } stored
            ? new ActionThresholdSetting(stored, row.UpdatedBy, row.UpdatedAt)
            : new ActionThresholdSetting(ActionThresholds.Defaults, null, null);
    }

    public async ValueTask SetAsync(ActionThresholds thresholds, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        var value = JsonSerializer.Serialize(thresholds, Json);
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Id == AppSettingKeys.PortfolioActionThresholds, cancellationToken);
        if (row is null)
        {
            db.AppSettings.Add(AppSetting.Create(AppSettingKeys.PortfolioActionThresholds, value, actor, now));
        }
        else
        {
            row.Change(value, actor, now);
        }
    }

    /// <summary>The stored thresholds; null when the text is not JSON of the right shape or a value is out of range.</summary>
    private static ActionThresholds? Read(string text)
    {
        try
        {
            var thresholds = JsonSerializer.Deserialize<ActionThresholds>(text, Json);
            if (thresholds is null)
            {
                return null;
            }

            var errors = new FieldErrors();
            thresholds.Validate(errors);
            return errors.HasErrors ? null : thresholds;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
