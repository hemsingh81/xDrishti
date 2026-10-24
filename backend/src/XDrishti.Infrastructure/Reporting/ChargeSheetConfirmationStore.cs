using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Reporting;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Reporting;

/// <summary>
/// The owner's stored confirmation of the charge sheet (ISS-25, P8-00 question 19, ADR 0100): one settings row, <see cref="AppSettingKeys.ReportsChargeSheetConfirmation"/>, holding <c>{"sheet_id":"...","rates_sha256":"64 hex","confirmed_on":"yyyy-MM-dd"}</c>. It is read, never written, here:
/// the owner's act of confirming is not part of this slice. Fails closed: a missing row, a text that does not parse, a missing or malformed rates hash, a date that is not a date, a sheet id that is no stored sheet, or a stored sheet whose rates hash is no longer the confirmed one
/// is "not confirmed", so a report stays gross.
/// </summary>
internal sealed class ChargeSheetConfirmationStore(XDrishtiDbContext db, ICostSheetStore sheets) : IChargeSheetConfirmationSource
{
    public async ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken)
    {
        var text = await db.AppSettings.AsNoTracking().Where(x => x.Id == AppSettingKeys.ReportsChargeSheetConfirmation).Select(x => x.Value).FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return null;
        }

        string? sheetId;
        string? confirmedOn;
        string? rates;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            sheetId = root.TryGetProperty("sheet_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            confirmedOn = root.TryGetProperty("confirmed_on", out var on) && on.ValueKind == JsonValueKind.String ? on.GetString() : null;
            rates = root.TryGetProperty("rates_sha256", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sheetId) || !RunRecordText.IsHex64(rates) || !DateOnly.TryParseExact(confirmedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return null;
        }

        // The confirmation is of these rates: a sheet saved again under the same id with other rates (the store overwrites in place) is not what the owner confirmed.
        var sheet = await sheets.FindAsync(sheetId, cancellationToken);
        return sheet.IsSuccess && string.Equals(sheet.Value.RatesSha256, rates, StringComparison.Ordinal) ? new ChargeSheetConfirmation(sheetId, rates!, date) : null;
    }
}
