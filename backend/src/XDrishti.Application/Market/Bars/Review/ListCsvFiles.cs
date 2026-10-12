using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>
/// The files of the CSV folder for the review step (ADR 0017 §6): state, size, modified time, the symbol parsed from the name, the instrument it maps to (or why not) and, for loaded files, rows, first and last bar and
/// load time. Reads the directory entries and the log only — no file is opened — so a folder of hundreds of files lists at once. Changed and pending files come first, then loaded ones, each by name.
/// <c>q</c> (file name or symbol, substring), <c>state</c> (<c>pending|loaded|changed</c>), <c>page</c> from 1, <c>pageSize</c> up to 200 (default 50).
/// </summary>
public sealed record ListCsvFilesQuery(CsvFileRequest Request) : IQuery<Result<CsvFilePage>>;

internal sealed class ListCsvFilesHandler(CsvFileCatalog catalog, ICsvFolder folder) : IQueryHandler<ListCsvFilesQuery, Result<CsvFilePage>>
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxTextLength = 100;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<CsvFilePage>> HandleAsync(ListCsvFilesQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var errors = new FieldErrors();
        var text = request.Q?.Trim();
        if (text is { Length: > MaxTextLength })
        {
            errors.Add("q", $"Use at most {MaxTextLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("q", "Control characters are not allowed.");
        }

        var state = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim().ToLowerInvariant();
        if (state is not null && !CsvFileStates.IsKnown(state))
        {
            errors.Add("state", "Use pending, loaded or changed.");
        }

        var page = request.Page ?? 1;
        if (page is < 1 or > MaxPage)
        {
            errors.Add("page", $"Use 1 to {MaxPage}.");
        }

        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {MaxPageSize}.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<CsvFilePage>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."));
        }

        var listing = await catalog.ListAsync(cancellationToken);
        var matching = string.IsNullOrEmpty(text)
            ? listing.Files
            : [.. listing.Files.Where(f => f.File.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || (f.Symbol?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))];
        var counts = new CsvFileCounts(
            matching.Count,
            matching.Count(f => f.State == CsvFileStates.Pending),
            matching.Count(f => f.State == CsvFileStates.Loaded),
            matching.Count(f => f.State == CsvFileStates.Changed),
            matching.Count(f => f.Mapping != CsvFileMappings.Mapped));

        var filtered = state is null ? matching : [.. matching.Where(f => f.State == state)];
        var items = filtered
            .OrderBy(f => f.State == CsvFileStates.Loaded ? 1 : 0)
            .ThenBy(f => f.File.Name, StringComparer.Ordinal)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => Row(f, listing))
            .ToList();
        return Result.Success(new CsvFilePage(folder.Path, listing.Exists, items, page, pageSize, filtered.Count, counts));
    }

    private static CsvFileRow Row(CsvFileEntry entry, CsvFolderListing listing) => new(
        entry.File.Name,
        entry.State,
        entry.File.Size,
        entry.File.ModifiedAt,
        entry.Symbol,
        entry.Mapping,
        entry.Instrument,
        entry.Loaded?.Rows,
        entry.Loaded?.FirstBar,
        entry.Loaded?.LastBar,
        entry.Loaded?.LoadedAt,
        entry.Loaded is null ? null : listing.Overlaps?.GetValueOrDefault(entry.File.Name) ?? []);
}
