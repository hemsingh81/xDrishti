using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The identity of an expression run (spec P14-08): SHA-256 of <c>expr-remark/1</c>, the cash run key, the profile hash, the re-mark version, the archive watermark (the highest <c>options.fetch_log.id</c> read), a hash of the usable key-sessions read (the quality
/// verdicts carry no run id, so their content stands for them), the spread model ids and the owner's assumptions, the cost sheet's <c>RatesSha256</c> (or <c>gross</c>), and hashes of the lot rows, the epoch rows and the contract expiries read, and the settings the numbers
/// depend on. The same inputs give the same id, whatever the order they were read in; nothing is written twice.
/// </summary>
public static class RemarkRunId
{
    public const string Format = "expr-remark/1";
    public const string Version = "expr-1";

    public static string Of(string cashRunKey, string profileHash, long watermark, string usableSha, IEnumerable<string> spreadInputs, string sheetOrGross, string lotsSha, string epochsSha, string contractsSha, string settings)
    {
        ArgumentNullException.ThrowIfNull(spreadInputs);
        var lines = new List<string>
        {
            Format, cashRunKey, profileHash, Version, watermark.ToString(CultureInfo.InvariantCulture), usableSha, string.Join(',', spreadInputs.Order(StringComparer.Ordinal)), sheetOrGross, lotsSha, epochsSha, contractsSha, settings,
        };
        return CanonicalText.Sha256Hex(string.Join('\n', lines));
    }

    public static string LotsSha(IEnumerable<LotSizeEntry> lots) =>
        CanonicalText.Sha256Hex(string.Join('\n', lots.OrderBy(l => l.UnderlyingId).ThenBy(l => l.EffectiveFrom).ThenBy(l => l.Id).Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.UnderlyingId:N}|{l.EffectiveFrom:yyyy-MM-dd}|{l.LotSize}|{l.Source}|{l.Confirmed}"))));

    public static string EpochsSha(IEnumerable<MarketEpoch> epochs) =>
        CanonicalText.Sha256Hex(string.Join('\n', epochs.OrderBy(e => e.Code, StringComparer.Ordinal).ThenBy(e => e.EffectiveFrom).Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Code}|{e.EffectiveFrom:yyyy-MM-dd}|{e.Confirmed}"))));

    public static string ContractsSha(IEnumerable<ContractExpiry> expiries) =>
        CanonicalText.Sha256Hex(string.Join('\n', expiries.OrderBy(e => e.Date).ThenBy(e => e.Flag).Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Date:yyyy-MM-dd}|{e.Flag}"))));

    /// <summary>A hash of the usable key-sessions read: <c>fetch_key|date</c> lines in order.</summary>
    public static string UsableSha(IEnumerable<string> keySessions) => CanonicalText.Sha256Hex(string.Join('\n', keySessions.Order(StringComparer.Ordinal)));
}
