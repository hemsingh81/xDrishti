using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Closes a linked lesson with the before and after numbers read from the stored experiment of its fix (P6-16); the caller states no number.</summary>
/// <param name="Id">The lesson id.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">Who closes; only the owner may.</param>
public sealed record CloseLessonCommand(long Id, string Reason, string Actor) : ICommand;
