using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>A sheet store held in memory that counts its calls and remembers the order they came in relative to the other ports (the test reads <see cref="Finds"/>).</summary>
internal sealed class FakeCostSheetStore : ICostSheetStore
{
    private readonly Dictionary<string, CostSheet> _sheets = new(StringComparer.Ordinal);

    public List<string> Finds { get; } = [];

    public List<(CostSheet Sheet, string Actor, DateTimeOffset Now)> Saves { get; } = [];

    /// <summary>When set, <see cref="FindAsync"/> answers this error for every id (a stored text that no longer parses).</summary>
    public Error? Failure { get; set; }

    /// <summary>Called on every find, before it answers (a test uses it to look at the other ports).</summary>
    public Action? OnFind { get; set; }

    public FakeCostSheetStore With(CostSheet sheet)
    {
        _sheets[sheet.Id] = sheet;
        return this;
    }

    public ValueTask<Result<CostSheet>> FindAsync(string sheetId, CancellationToken cancellationToken)
    {
        Finds.Add(sheetId);
        OnFind?.Invoke();
        if (Failure is { } failure)
        {
            return ValueTask.FromResult(Result.Failure<CostSheet>(failure));
        }

        return ValueTask.FromResult(_sheets.TryGetValue(sheetId, out var sheet) ? Result.Success(sheet) : Result.Failure<CostSheet>(CostErrors.SheetNotFound(sheetId)));
    }

    public ValueTask SaveAsync(CostSheet sheet, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Saves.Add((sheet, actor, now));
        _sheets[sheet.Id] = sheet;
        return ValueTask.CompletedTask;
    }
}
