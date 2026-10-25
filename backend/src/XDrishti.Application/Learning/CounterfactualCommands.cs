using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>
/// Evaluates the frozen-model counterfactual (as <see cref="EvaluateCounterfactualQuery"/>) and stores the result once per input hash in the insert-only learning reports (spec P8-12, ADR 0110). The only write is the report: nothing is applied and no order is placed.
/// </summary>
/// <param name="Query">The evaluation to run.</param>
/// <param name="Actor">Who asked (<c>cli:xd</c>).</param>
public sealed record StoreCounterfactualCommand(EvaluateCounterfactualQuery Query, string Actor) : ICommand;

/// <summary>What storing did.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Created">False when the same inputs were stored already: nothing was written.</param>
/// <param name="Text">The printed text of the stored report.</param>
public sealed record StoredCounterfactual(long Id, bool Created, string Text);

/// <summary>One stored counterfactual: the one with the id, or the newest when <paramref name="Id"/> is null. Read only.</summary>
/// <param name="Id">The row id, or null for the newest.</param>
public sealed record GetCounterfactualQuery(long? Id) : IQuery<Result<CounterfactualReportView>>;

/// <summary>The stored counterfactuals, newest first. Read only.</summary>
/// <param name="Limit">1 to 50.</param>
public sealed record ListCounterfactualsQuery(int Limit) : IQuery<Result<IReadOnlyList<CounterfactualHeaderView>>>;

/// <summary>The Journey: the newest stored counterfactual, the plan-price weeks and the live line. Read only.</summary>
public sealed record GetJourneyQuery : IQuery<Result<JourneyView>>;
