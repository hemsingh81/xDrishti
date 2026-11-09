using System.Globalization;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>The per-session proof stored in <c>recovery_items.detail</c> (spec AU-03): the sessions, whether the quality run was verified and when the proved run started (so a later look can prove again).</summary>
internal sealed record ProofDetail(IReadOnlyList<SessionProof> Sessions, bool QualityVerified, DateTimeOffset RunStartedAt);

/// <summary>Writes, reads and applies the proof of a recovery item. JSON is written by hand (no reflection serialisation); jsonb may reorder the keys, so reading is by name.</summary>
internal static class RecoveryProofDetail
{
    public static string Write(IReadOnlyList<SessionProof> proofs, bool qualityVerified, DateTimeOffset runStartedAt)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteBoolean("qualityVerified", qualityVerified);
            w.WriteString("runStartedAt", runStartedAt.ToString("O", CultureInfo.InvariantCulture));
            w.WriteStartArray("sessions");
            foreach (var p in proofs)
            {
                w.WriteStartObject();
                w.WriteString("date", p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteString("state", p.State);
                w.WriteNumber("instruments", p.Instruments);
                w.WriteNumber("complete", p.Complete);
                w.WriteNumber("unfillable", p.Unfillable);
                w.WriteNumber("missing", p.Missing);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The stored proof, or null when there is none or it cannot be read (then nothing is assumed).</summary>
    public static ProofDetail? Read(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(detail);
            var root = doc.RootElement;
            var sessions = new List<SessionProof>();
            foreach (var s in root.GetProperty("sessions").EnumerateArray())
            {
                sessions.Add(new SessionProof(
                    DateOnly.ParseExact(s.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    s.GetProperty("state").GetString()!,
                    s.GetProperty("instruments").GetInt32(),
                    s.GetProperty("complete").GetInt32(),
                    s.GetProperty("unfillable").GetInt32(),
                    s.GetProperty("missing").GetInt32()));
            }

            return new ProofDetail(sessions, root.GetProperty("qualityVerified").GetBoolean(), root.GetProperty("runStartedAt").GetDateTimeOffset());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The item with the outcome of a proof: <c>succeeded</c> when everything is recovered, <c>partial</c> otherwise.</summary>
    public static RecoveryItemRow Apply(RecoveryItemRow item, RecoveryProofResult result) =>
        result.AllRecovered
            ? item with { State = RecoveryStates.Succeeded, Reason = null, Detail = result.Detail }
            : item with { State = RecoveryStates.Partial, Reason = Clip(result.Reason ?? "sessions not recovered"), Detail = result.Detail };

    /// <summary>A proof that could not be read: partial (fail closed), with every session still missing so a later look proves it again.</summary>
    public static RecoveryItemRow Unverified(RecoveryItemRow item, IReadOnlyList<DateOnly> sessions, DateTimeOffset runStartedAt) =>
        item with
        {
            State = RecoveryStates.Partial,
            Reason = "the sessions could not be verified (a store read failed); they are checked again at the next pass",
            Detail = Write([.. sessions.Select(d => new SessionProof(d, SessionProofStates.StillMissing, 0, 0, 0, 0))], false, runStartedAt),
        };

    /// <summary>Proves the sessions of a stored partial item again; null when its detail cannot be read. Throws when the stores cannot be read.</summary>
    public static async ValueTask<RecoveryProofResult?> ReproveAsync(IRecoveryProof proof, RecoveryItemRow item, CancellationToken cancellationToken)
    {
        if (Read(item.Detail) is not { Sessions.Count: > 0 } detail)
        {
            return null;
        }

        return await proof.ProveAsync([.. detail.Sessions.Select(s => s.Date)], detail.RunStartedAt, cancellationToken);
    }

    private static string Clip(string text) => text.Length <= 400 ? text : text[..399] + "~";
}
