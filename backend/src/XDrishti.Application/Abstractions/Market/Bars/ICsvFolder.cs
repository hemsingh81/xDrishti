namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>A file of the CSV folder as the file system reports it.</summary>
public sealed record CsvFileInfo(string Name, long Size, DateTimeOffset ModifiedAt);

/// <summary>The folder with the 1-minute CSV files (mounted read-only). Reading only.</summary>
public interface ICsvFolder
{
    /// <summary>Where the folder is, for display.</summary>
    string Path { get; }

    bool Exists();

    /// <summary>The files directly in the folder (no sub-folders), in any order.</summary>
    ValueTask<IReadOnlyList<CsvFileInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Opens the file for sequential reading (the caller disposes the stream); the stream is the raw file, a <c>.gz</c> file is not decompressed.</summary>
    Stream Open(string name);
}
