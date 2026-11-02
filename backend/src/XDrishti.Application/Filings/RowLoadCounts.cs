using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>The running counts of one loader run; <see cref="Insert"/> maps a store outcome onto them.</summary>
internal sealed class RowLoadCounts(string kind)
{
    /// <summary>How many instruments a loader stores at once (one connection each).</summary>
    public const int Parallelism = 8;

    private readonly List<RowLoadFile> _files = [];

    // The loaders store different instruments in parallel, so every mutation takes this lock.
    private readonly Lock _gate = new();

    public int Files { get; set; }

    public int Loaded { get; private set; }

    public int Revisions { get; private set; }

    public int OutOfOrder { get; private set; }

    public int Duplicate { get; private set; }

    public int Unavailable { get; private set; }

    public int SumOff { get; private set; }

    public int Failures { get; set; }

    public Dictionary<string, int> Rejected { get; } = new(StringComparer.Ordinal);

    public const int MaxDetails = 50;

    private readonly List<RowReject> _details = [];

    /// <summary>Counts a rejected row or group and keeps its line, reason and column (the first <see cref="MaxDetails"/> of the file).</summary>
    public void Reject(RowReject reject)
    {
        ArgumentNullException.ThrowIfNull(reject);
        Reject(reject.Reason);
        lock (_gate)
        {
            if (_details.Count < MaxDetails)
            {
                _details.Add(reject);
            }
        }
    }

    public void Reject(string reason, int count = 1)
    {
        if (count > 0)
        {
            lock (_gate)
            {
                Rejected[reason] = Rejected.GetValueOrDefault(reason) + count;
            }
        }
    }

    /// <summary>Counts one store result; returns the closed reject reason when the row or group was not stored for a reason of its own (else null).</summary>
    public string? Insert(FilingInsertResult result, bool undated, int line)
    {
        string? reason;
        lock (_gate)
        {
            reason = InsertLocked(result, undated);
        }

        if (reason is not null)
        {
            lock (_gate)
            {
                if (_details.Count < MaxDetails)
                {
                    _details.Add(new RowReject(line, reason));
                }
            }
        }

        return reason;
    }

    public void AddSumOff()
    {
        lock (_gate)
        {
            SumOff++;
        }
    }

    private string? InsertLocked(FilingInsertResult result, bool undated)
    {
        switch (result.Outcome)
        {
            case FilingInsertOutcome.Stored:
            case FilingInsertOutcome.StoredOutOfOrder:
                Loaded++;
                Revisions += result.Supersedes is null ? 0 : 1;
                OutOfOrder += result.Outcome == FilingInsertOutcome.StoredOutOfOrder ? 1 : 0;
                Unavailable += undated ? 1 : 0;
                return null;
            case FilingInsertOutcome.Duplicate:
                Duplicate++;
                return null;
            case FilingInsertOutcome.AmbiguousOrder:
                Rejected["ambiguous_order"] = Rejected.GetValueOrDefault("ambiguous_order") + 1;
                return "ambiguous_order";
            case FilingInsertOutcome.BadSupersede:
                Rejected["bad_supersede"] = Rejected.GetValueOrDefault("bad_supersede") + 1;
                return "bad_supersede";
            default:
                Rejected["store_conflict"] = Rejected.GetValueOrDefault("store_conflict") + 1;
                return "store_conflict";
        }
    }

    public (int Loaded, int Duplicate, int Rejected) Mark()
    {
        lock (_gate)
        {
            _details.Clear();
            return (Loaded, Duplicate, Rejected.Values.Sum());
        }
    }

    public void EndFile(string fileName, string? fileReject, int rows, (int Loaded, int Duplicate, int Rejected) before) =>
        _files.Add(new RowLoadFile(fileName, fileReject, rows, Loaded - before.Loaded, Duplicate - before.Duplicate, Rejected.Values.Sum() - before.Rejected, [.. _details.OrderBy(d => d.Line)]));

    public RowLoadSummary ToSummary() => new(kind, Files, Loaded, Revisions, OutOfOrder, Duplicate, Unavailable, SumOff, Rejected, Failures, _files);
}
