namespace XDrishti.Application.Abstractions.Filings;

/// <summary>A file the inbox holds and the system refuses, with a closed reason (<c>FilingRejectReasons</c>) and, for an invalid sidecar, the closed code of the first problem. Never any content of the file.</summary>
public sealed record RejectedFiling(string FileName, string Reason, string? Detail);
