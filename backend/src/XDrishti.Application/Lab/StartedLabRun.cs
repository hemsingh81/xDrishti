namespace XDrishti.Application.Lab;

/// <summary>The answer of a post: the run and whether it was made now (<c>202</c>) or was the run that already held the key (<c>200</c>).</summary>
public sealed record StartedLabRun(LabRunView Run, bool Created);
