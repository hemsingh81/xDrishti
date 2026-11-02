namespace XDrishti.Application.Filings;

/// <summary>The file name kept on an envelope made from a csv row (the column holds 120 characters).</summary>
internal static class FileLabel
{
    public const int MaxLength = 120;

    public static string Of(string fileName) => fileName.Length <= MaxLength ? fileName : fileName[..MaxLength];
}
