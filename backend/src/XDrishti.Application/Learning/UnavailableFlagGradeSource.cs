using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>The flag grade before the announcement eval exists (P15-08): there is no accepted set D run, so protocol L answers NOT_EVALUABLE. The eval slice registers the real one in its place.</summary>
internal sealed class UnavailableFlagGradeSource : IFlagGradeSource
{
    public ValueTask<FlagGrade?> NewestAsync(CancellationToken cancellationToken) => ValueTask.FromResult<FlagGrade?>(null);
}
