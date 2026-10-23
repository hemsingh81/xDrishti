using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One registered model with its report, calibration table, importance and designations (P6-07).</summary>
/// <param name="Id">The registry id.</param>
public sealed record GetModelQuery(long Id) : IQuery<Result<ModelDetailView>>;
