using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>What the stored plans say about each smart rule (P8-13). Read only.</summary>
/// <param name="Plans">How many planned sessions to read, newest first, 1 to 60.</param>
public sealed record GetSmartRulesEvidenceQuery(int Plans) : IQuery<Result<SmartRulesView>>;
