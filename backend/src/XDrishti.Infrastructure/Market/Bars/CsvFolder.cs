using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>The mounted folder with the 1-minute CSV files (read-only). Modified times are cut to milliseconds, which is what the database keeps comparably.</summary>
internal sealed class CsvFolder(IDataSettings settings) : ICsvFolder
{
    public string Path => settings.CsvFolder;

    public bool Exists() => Directory.Exists(Path);

    public ValueTask<IReadOnlyList<CsvFileInfo>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CsvFileInfo> files = [.. new DirectoryInfo(Path).EnumerateFiles().Select(f => new CsvFileInfo(f.Name, f.Length, Truncate(f.LastWriteTimeUtc)))];
        return ValueTask.FromResult(files);
    }

    public Stream Open(string name)
    {
        // The name comes from the folder listing; refuse anything that could leave the folder all the same.
        if (name != System.IO.Path.GetFileName(name))
        {
            throw new ArgumentException("A file name is expected, not a path.", nameof(name));
        }

        return new FileStream(System.IO.Path.Combine(Path, name), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 1 << 16,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
    }

    private static DateTimeOffset Truncate(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
}
