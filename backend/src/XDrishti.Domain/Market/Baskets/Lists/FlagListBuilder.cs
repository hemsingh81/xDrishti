namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>The master fields the lists read for one share (retired instruments are never members).</summary>
public sealed record FlagRow(Guid Id, string? Series, bool FnoEligible, string? AsmGsmFlag, string? AsmGsmCategory, decimal? MtfLeverage, bool Retired);

/// <summary>A member of a list and its note (the ASM/GSM category), if any.</summary>
public sealed record ListMember(Guid InstrumentId, string? Note);

/// <summary>Builds the members of the four master-derived lists from the stored master fields; pure set arithmetic over rows already in memory.</summary>
public static class FlagListBuilder
{
    /// <summary>The series that mean SME or trade-for-trade.</summary>
    public static readonly IReadOnlySet<string> TradeForTradeSeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BE", "BZ", "SM", "ST" };

    public static IReadOnlyDictionary<string, IReadOnlyList<ListMember>> Build(IEnumerable<FlagRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var fno = new List<ListMember>();
        var asm = new List<ListMember>();
        var mtf = new List<ListMember>();
        var t2t = new List<ListMember>();
        foreach (var row in rows.Where(r => !r.Retired).OrderBy(r => r.Id))
        {
            if (row.FnoEligible)
            {
                fno.Add(new ListMember(row.Id, null));
            }

            if (IsFlagged(row.AsmGsmFlag))
            {
                asm.Add(new ListMember(row.Id, Note(row)));
            }

            if (row.MtfLeverage is > 0)
            {
                mtf.Add(new ListMember(row.Id, null));
            }

            if (row.Series is { } series && TradeForTradeSeries.Contains(series.Trim()))
            {
                t2t.Add(new ListMember(row.Id, null));
            }
        }

        return new Dictionary<string, IReadOnlyList<ListMember>>(StringComparer.Ordinal)
        {
            [ListBaskets.FnoStocks.Key] = fno,
            [ListBaskets.AsmGsm.Key] = asm,
            [ListBaskets.Mtf.Key] = mtf,
            [ListBaskets.TradeForTrade.Key] = t2t,
        };
    }

    /// <summary>Anything but <c>N</c> (and not blank) counts as a surveillance flag.</summary>
    public static bool IsFlagged(string? flag) => !string.IsNullOrWhiteSpace(flag) && !flag.Trim().Equals("N", StringComparison.OrdinalIgnoreCase);

    private static string Note(FlagRow row)
    {
        var text = string.IsNullOrWhiteSpace(row.AsmGsmCategory) ? row.AsmGsmFlag!.Trim() : row.AsmGsmCategory.Trim();
        return text.Length <= BasketLimits.MaxNoteLength ? text : text[..BasketLimits.MaxNoteLength];
    }
}
