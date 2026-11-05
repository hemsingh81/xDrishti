using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Shadow;

/// <summary>
/// One candidate of a nightly plan, recorded before its session (spec CF-08, D4). Pure. <see cref="RowHash"/> is the SHA-256 of the canonical JSON of every field written by the application, the plan id included; the database adds only the id and the written-at instant,
/// which are not hashed (the database stamps the second, so it cannot be known before the insert). Levels, features and the probability come only from the stored plan record, else they are null.
/// </summary>
/// <param name="SessionDate">The session the plan is for.</param>
/// <param name="PlanId">The stored plan.</param>
/// <param name="PlanItemId">The stored plan item.</param>
/// <param name="TradeKey">The key the paper record uses for the same ticket (<see cref="ShadowTradeKey"/>).</param>
/// <param name="TicketGroup"><c>primary</c>, <c>reserve</c> or <c>rejected</c>.</param>
/// <param name="CellKey">The cell the ticket comes from.</param>
/// <param name="StrategyHash">The strategy version.</param>
/// <param name="Symbol">The instrument symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="EntryLimit">The indicative entry reference of the plan, or null.</param>
/// <param name="Stop">The numeric stop of the plan, or null.</param>
/// <param name="Target">The numeric target of the plan, or null.</param>
/// <param name="TimeRule">The validity window of the plan as text.</param>
/// <param name="ConfigHash">The frozen configuration fingerprint in force (ADR 0119), or null when none is stored.</param>
/// <param name="ModelId">The champion model id, or null when the plan holds none.</param>
/// <param name="PWin">The model's advisory probability of the plan record, or null.</param>
/// <param name="FeatureVersion">The feature version, or null when the plan holds no feature vector.</param>
/// <param name="Features">The feature vector, or null when the plan holds none.</param>
/// <param name="CountsForEdge">Whether the row is edge evidence; false for a duplicate or a candidate that never reached the fill.</param>
/// <param name="EdgeEpoch">The paper epoch in force when written, or null when there is none yet.</param>
public sealed record ShadowEntry(
    DateOnly SessionDate,
    long PlanId,
    long PlanItemId,
    string TradeKey,
    string TicketGroup,
    string CellKey,
    string StrategyHash,
    string Symbol,
    string Side,
    decimal? EntryLimit,
    decimal? Stop,
    decimal? Target,
    string TimeRule,
    string? ConfigHash,
    string? ModelId,
    double? PWin,
    string? FeatureVersion,
    IReadOnlyDictionary<string, double>? Features,
    bool CountsForEdge,
    long? EdgeEpoch)
{
    /// <summary>The canonical JSON the hash is taken over: fixed field order, decimals with four places, numbers as invariant round-trip text, feature keys in ordinal order.</summary>
    public string CanonicalJson
    {
        get
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("session_date", SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                json.WriteNumber("plan_id", PlanId);
                json.WriteNumber("plan_item_id", PlanItemId);
                json.WriteString("trade_key", TradeKey);
                json.WriteString("ticket_group", TicketGroup);
                json.WriteString("cell_key", CellKey);
                json.WriteString("strategy_hash", StrategyHash);
                json.WriteString("symbol", Symbol);
                json.WriteString("side", Side);
                Text(json, "entry_limit", Level(EntryLimit));
                Text(json, "stop", Level(Stop));
                Text(json, "target", Level(Target));
                json.WriteString("time_rule", TimeRule);
                Text(json, "config_hash", ConfigHash);
                Text(json, "model_id", ModelId);
                Text(json, "p_win", PWin?.ToString("R", CultureInfo.InvariantCulture));
                Text(json, "feature_version", FeatureVersion);
                if (Features is null)
                {
                    json.WriteNull("features");
                }
                else
                {
                    json.WriteStartObject("features");
                    foreach (var pair in Features.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        json.WriteString(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
                    }

                    json.WriteEndObject();
                }

                json.WriteBoolean("counts_for_edge", CountsForEdge);
                if (EdgeEpoch is { } epoch)
                {
                    json.WriteNumber("edge_epoch", epoch);
                }
                else
                {
                    json.WriteNull("edge_epoch");
                }

                json.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }

    /// <summary>The 64-character lower-case hex SHA-256 of <see cref="CanonicalJson"/>.</summary>
    public string RowHash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson)));

    private static string? Level(decimal? value) => value is { } v ? decimal.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.0000", CultureInfo.InvariantCulture) : null;

    private static void Text(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }
}
