namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>The one capped read of a response body for the tool server's API clients (ADR 0267, ADR 0280): the body, or null when it is larger than the limit (the declared length is checked first, the read is cut at the limit whatever the header says).</summary>
internal static class CappedBody
{
    public static async Task<byte[]?> ReadAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength > limit)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
