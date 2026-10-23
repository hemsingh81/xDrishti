using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>Links a fix to an open lesson (P6-16): a hypothesis or an experiment that exists. Linking applies nothing.</summary>
/// <param name="Id">The lesson id.</param>
/// <param name="FixKind"><c>hypothesis</c> or <c>experiment</c>.</param>
/// <param name="FixId">The hypothesis or experiment id.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">Who links; only the owner (<c>cli:xd</c>, <c>owner:...</c>) may.</param>
public sealed record LinkLessonCommand(long Id, string FixKind, long FixId, string Reason, string Actor) : ICommand;
