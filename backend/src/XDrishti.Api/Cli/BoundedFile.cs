using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The bounded file read of the verbs (it was the <c>cost-sheet</c> verb's, dropped by ADR 0292; the <c>lab</c> verb reads its strategy file with it).</summary>
internal static class BoundedFile
{
    /// <summary>
    /// Reads a file of at most <paramref name="max"/> bytes. A path that is not a regular, non-empty file (a directory, a device, a FIFO, a socket or a /proc file all report length 0 or do not exist as files) is refused without being read; a symbolic
    /// link to a regular file is fine. The read itself stops at <paramref name="max"/> + 1 bytes, so a file that grew after the length was looked at, or one that lied about it, is refused too.
    /// </summary>
    internal static async Task<(byte[]? Bytes, string? Problem)> ReadAsync(string path, int max, string what, CancellationToken cancellationToken)
    {
        var name = SimulateOptions.Show(Path.GetFileName(path));
        long length;
        try
        {
            var info = new FileInfo(path);
            length = info.Exists ? info.Length : -1;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            length = -1;
        }

        if (length <= 0)
        {
            return (null, $"{what} cannot be read: '{name}' is not a regular, non-empty file.");
        }

        if (length > max)
        {
            return (null, $"{what} is {length} bytes; the most read is {max} bytes. It was not read.");
        }

        try
        {
            await using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite, Options = FileOptions.Asynchronous });
            var buffer = new byte[max + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (n == 0)
                {
                    break;
                }

                count += n;
            }

            return count > max
                ? (null, $"{what} grew past {max} bytes while it was read. It was not used.")
                : (buffer[..count], null);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (null, $"{what} cannot be read: '{name}'.");
        }
    }
}
