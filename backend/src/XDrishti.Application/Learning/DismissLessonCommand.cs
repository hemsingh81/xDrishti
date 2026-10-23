using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Sets an open or linked lesson aside without a result (P6-16).</summary>
/// <param name="Id">The lesson id.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">Who dismisses; only the owner may.</param>
public sealed record DismissLessonCommand(long Id, string Reason, string Actor) : ICommand;
