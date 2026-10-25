using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Store one entry-window study (P8-13b, ADR 0112) once per input hash. The only write is the report; nothing is applied.</summary>
/// <param name="Query">The study to run.</param>
/// <param name="Actor">Who asked (<c>cli:xd</c>).</param>
public sealed record StoreEntryWindowsCommand(ProposeEntryWindowsQuery Query, string Actor) : ICommand;

/// <summary>Store one meta-policy evaluation (P8-13b, ADR 0112) once per input hash. A NoPolicy result is stored like any other.</summary>
/// <param name="Query">The evaluation to run.</param>
/// <param name="Actor">Who asked (<c>cli:xd</c>).</param>
public sealed record StoreMetaPolicyCommand(EvaluateMetaPolicyQuery Query, string Actor) : ICommand;

/// <summary>What storing did.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Created">False when the same inputs were stored already.</param>
/// <param name="Text">The printed report.</param>
public sealed record StoredReport(long Id, bool Created, string Text);

/// <summary>A stored entry-window study: the one with the id, else the newest. Read only.</summary>
/// <param name="Id">The row id, or null for the newest.</param>
public sealed record GetEntryWindowsQuery(long? Id) : IQuery<Result<EntryWindowReportView>>;

/// <summary>A stored meta-policy evaluation: the one with the id, else the newest. Read only.</summary>
/// <param name="Id">The row id, or null for the newest.</param>
public sealed record GetMetaPolicyQuery(long? Id) : IQuery<Result<MetaPolicyReportView>>;

/// <summary>Kinds, input hashes and the read of the generic stored reports for entry windows and the meta policy (P8-13b).</summary>
internal static class StoredReportKinds
{
    public const string EntryWindows = "entry_windows";

    public const string MetaPolicy = "meta_policy";

    public const string EntryWindowsNotFoundCode = "learning.entry_windows_not_found";

    public const string MetaPolicyNotFoundCode = "learning.meta_policy_not_found";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The printed text with the "limit:" line the table requires.</summary>
    public static string TextOf(string text) => text.Contains("limit:", StringComparison.Ordinal) ? text : text + "limit: none recorded" + Environment.NewLine;

    public static string Sources(IReadOnlyList<CellSource> sources) => string.Join(',', sources.Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}").Order(StringComparer.Ordinal));

    /// <summary>The sources in the order they are stored and hashed: by run key, then style, then exit.</summary>
    public static List<CellSource> Ordered(IReadOnlyList<CellSource> sources) =>
        [.. sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).ThenBy(s => s.Style, StringComparer.Ordinal).ThenBy(s => s.Exit, StringComparer.Ordinal)];

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // shortcut: the fold options Trials, HoldOutStart, MinFoldsForVerdict and the margins are not hashed; the verbs pass the defaults, so they cannot differ. Hash them if a caller ever sets them.
    public static string WindowsHash(ProposeEntryWindowsQuery q)
    {
        var o = q.Options;
        var v = q.Validation;
        return Sha(string.Create(CultureInfo.InvariantCulture, $"entry_windows|v1|{Sources(q.Sources)}|{o.BucketMinutes}|{o.MinWindowTrades}|{o.MinLift:R}|{o.PriorStrength:R}|{o.MinFoldTrades}|{o.MinEvaluableFolds}|holdout {q.HoldOutMonths}|{v.MinTrainMonths}|{v.TestMonths}|{v.EmbargoDays}|{v.MinTrainTrades}"));
    }

    /// <summary>Uses the effective options of the report (the champion's trial count may have raised them), the trainer recipe and the champion line (or the NoPolicy reason).</summary>
    public static string PolicyHash(EvaluateMetaPolicyQuery q, MetaPolicyReport r, string trainer)
    {
        var o = r.Options;
        var v = q.Validation;
        var champion = r.Limits.FirstOrDefault(l => l.StartsWith("Champion:", StringComparison.Ordinal)) ?? "no champion line: " + string.Join(' ', r.Reasons);
        return Sha(string.Create(CultureInfo.InvariantCulture, $"meta_policy|v1|{Sources(q.Sources)}|{o.Split}|{o.MinKeptShare:R}|{o.ThresholdStep:R}|{o.MinThresholdRows}|{o.MinLiftR:R}|{o.MinEvaluableFolds}|{o.LastTrades}|{o.MinSideTrades}|{o.MinSkillR:R}|{o.SignificanceZ:R}|{o.Trials}|holdout {q.HoldOutMonths}|{v.MinTrainMonths}|{v.TestMonths}|{v.EmbargoDays}|{v.MinTrainTrades}|{trainer}|{champion}"));
    }

    /// <summary>Reads a stored view of a kind; 404 with the kind's code when none or unreadable.</summary>
    public static async ValueTask<Result<T>> ReadAsync<T>(ILearningReportStore store, string kind, long? id, string notFoundCode, string what, Func<T, StoredLearningReport, T> stamp, CancellationToken cancellationToken)
        where T : class
    {
        var stored = id is { } n ? await store.FindAsync(n, cancellationToken) : await store.NewestAsync(kind, cancellationToken);
        if (stored is null || stored.Report.Kind != kind)
        {
            return Result.Failure<T>(Error.NotFound(notFoundCode, id is { } i ? $"No stored {what} #{i}." : $"No {what} is stored yet."));
        }

        var view = JsonSerializer.Deserialize<T>(stored.Report.ReportJson, Json);
        return view is null ? Result.Failure<T>(Error.NotFound(notFoundCode, $"The stored {what} cannot be read.")) : Result.Success(stamp(view, stored));
    }
}
