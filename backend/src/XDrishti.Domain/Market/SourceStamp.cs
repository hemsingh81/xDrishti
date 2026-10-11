namespace XDrishti.Domain.Market;

/// <summary>The validators a source file was served with (HTTP <c>ETag</c> and <c>Last-Modified</c>), used to ask "has it changed?" next time.</summary>
public sealed record SourceStamp(string? ETag, string? LastModified)
{
    public static readonly SourceStamp None = new(null, null);

    public bool IsEmpty => string.IsNullOrEmpty(ETag) && string.IsNullOrEmpty(LastModified);
}
