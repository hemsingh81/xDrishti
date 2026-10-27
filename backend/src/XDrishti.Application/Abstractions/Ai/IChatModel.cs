namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The local model as the turn loop uses it (spec P9-03): one streamed completion per call. The loop (budget, discard, audit) is the application's; the adapter only turns a <see cref="ModelRequest"/> into the model server's request and its stream into <see cref="ModelStreamPart"/>s.
/// A failure of the server surfaces as <see cref="ModelUnavailableException"/>; cancelling the token closes the connection to the server.
/// </summary>
public interface IChatModel
{
    IAsyncEnumerable<ModelStreamPart> StreamAsync(ModelRequest request, CancellationToken cancellationToken);
}
