using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Imports;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A demat holding statement read against the account: the statement, its comparison with the synced holdings, the names it teaches and a digest of all of it.</summary>
internal sealed record DematPrepared(DematFile File, DematComparison Comparison, IReadOnlyList<TaughtName> Missing, int KnownNames, string Digest);

/// <summary>
/// The demat holding statement as a check and a source of names (ADR 0029). It never creates or changes a holding - the broker's holdings API stays authoritative (ADR 0012). The preview compares the depository's quantities with the synced holdings
/// (<see cref="DematChecker"/>: securities that agree, quantity differences, held at Dhan but not synced, synced but not in the statement) and counts the names it would teach; the import remembers the names (ISIN-keyed, so a transaction report that has names only can find
/// ISINs). <b>No notice is raised for the differences: they are shown in the preview and the import result only</b> (the attention items are derived from stored facts, and the comparison is not one). Failures of the file are the preview's <c>Error</c>, never exceptions.
/// </summary>
internal sealed class DematService(
    ReportWorkspace reports,
    ISecurityAliases securityAliases,
    IAccountEventLog events,
    ReportClientCodes clientCodes,
    SyncGuard guard,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock)
{
    private const string Kind = FileKindDetector.DematHoldingCode;

    /// <summary>The most entries each list of a preview holds (the totals count all).</summary>
    public const int MaxListed = 200;

    public async ValueTask<ImportFilePreview> PreviewAsync(BrokerAccount account, string fileName, Sheet sheet, CancellationToken cancellationToken)
    {
        var read = DematHoldingReader.Read(sheet);
        if (read.IsFailure)
        {
            return ReportViews.Failed(fileName, Kind, read.Error);
        }

        var file = read.Value;
        var prepared = await PrepareAsync(account, file, cancellationToken);
        var c = prepared.Comparison;
        var demat = new DematPreview(
            file.AsOf,
            c.Securities,
            c.Matching,
            [.. c.Differences.Take(MaxListed)],
            c.Differences.Count,
            [.. c.HeldNotSynced.Take(MaxListed)],
            c.HeldNotSynced.Count,
            [.. c.SyncedNotInStatement.Take(MaxListed)],
            c.SyncedNotInStatement.Count,
            new NameLearning(prepared.KnownNames, prepared.Missing.Count));
        var warnings = new List<ImportWarning>();
        if (await clientCodes.IsOtherAccountAsync(account.Id, file.ClientCode, cancellationToken))
        {
            warnings.Add(ReportViews.OtherAccountWarning());
        }

        if (file.BadTotal > 0)
        {
            warnings.Add(new ImportWarning("bad_rows", "Some rows cannot be read and are not used; the first ones are listed with their line.", file.BadTotal));
        }

        var differing = c.Differences.Count + c.HeldNotSynced.Count + c.SyncedNotInStatement.Count;
        if (differing > 0)
        {
            warnings.Add(new ImportWarning("holdings_differ", "The statement and the synced holdings differ. Nothing is changed: the holdings Dhan reports stay as they are.", differing));
        }

        var summary = new ImportSummary(file.Rows, file.Items.Count, prepared.Missing.Count, prepared.KnownNames, 0, file.BadTotal, null, file.AsOf);
        return new ImportFilePreview(fileName, Kind, true, null, null, null, summary, warnings, null, null, null, demat, prepared.Digest);
    }

    /// <summary>Remembers the names of the statement. <paramref name="digest"/> is the preview's. No holding is touched. Refused (<c>409 portfolio.sync_running</c>) while a holdings sync or a broker history run of the account runs.</summary>
    public async ValueTask<Result<DematImportResult>> ImportAsync(BrokerAccount account, Sheet sheet, string digest, string actor, CancellationToken cancellationToken)
    {
        var read = DematHoldingReader.Read(sheet);
        if (read.IsFailure)
        {
            return Result.Failure<DematImportResult>(read.Error);
        }

        var file = read.Value;
        DematImportResult? result = null;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                // The names are compared with what is stored below inside this transaction; a broker history run or a holdings sync that is running teaches names of its own.
                if (await guard.RunningAsync(account.Id, token))
                {
                    return Result.Failure(PortfolioErrors.SyncRunning);
                }

                var at = clock.GetUtcNow();
                var prepared = await PrepareAsync(account, file, token);
                if (!string.Equals(digest.Trim(), prepared.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(ReportErrors.PreviewStale());
                }

                var learned = SecurityNames.Learn(securityAliases, events, account.Id, prepared.Missing, SecurityAliasSource.Demat, actor, at);
                await clientCodes.RememberAsync(account.Id, file.ClientCode, at, token);
                if (!await unitOfWork.TrySaveAsync(token))
                {
                    return Result.Failure(ReportErrors.NamesChanged);
                }

                result = new DematImportResult(learned, prepared.KnownNames);
                return Result.Success();
            },
            cancellationToken);
        return outcome.IsFailure ? Result.Failure<DematImportResult>(outcome.Error) : Result.Success(result!);
    }

    private async ValueTask<DematPrepared> PrepareAsync(BrokerAccount account, DematFile file, CancellationToken cancellationToken)
    {
        var data = await reports.ReadAsync(account, cancellationToken);
        var taught = SecurityNames.Of(file.Items.Select(r => (r.Name, r.Isin)));
        var missing = SecurityNames.Missing(data.Learned, taught);
        var comparison = DematChecker.Compare(file, data.Holdings);
        return new DematPrepared(file, comparison, missing, taught.Count - missing.Count, Digest(file, comparison, missing.Count));
    }

    private static string Digest(DematFile file, DematComparison comparison, int newNames)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, string.Create(CultureInfo.InvariantCulture, $"v1|{file.BadTotal}|{newNames}|{comparison.Matching}"));
        foreach (var d in comparison.Differences)
        {
            Append(hash, string.Create(CultureInfo.InvariantCulture, $"d|{d.Isin}|{d.Statement}|{d.Synced}"));
        }

        foreach (var h in comparison.HeldNotSynced)
        {
            Append(hash, string.Create(CultureInfo.InvariantCulture, $"h|{h.Isin}|{h.Quantity}"));
        }

        foreach (var s in comparison.SyncedNotInStatement)
        {
            Append(hash, string.Create(CultureInfo.InvariantCulture, $"s|{s.Symbol}|{s.Quantity}"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData("\n"u8);
    }
}
