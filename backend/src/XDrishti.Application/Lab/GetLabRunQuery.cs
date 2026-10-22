using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>One Lab run (spec P5-02); <c>lab.not_found</c> when it is not stored.</summary>
public sealed record GetLabRunQuery(Guid Id) : IQuery<Result<LabRunView>>;
