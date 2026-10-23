using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Lists registered models (spec P6-07). An empty list is an answer: nothing was registered yet.</summary>
internal sealed class ListModelsHandler(IModelRegistry registry) : IQueryHandler<ListModelsQuery, Result<IReadOnlyList<ModelView>>>
{
    public async ValueTask<Result<IReadOnlyList<ModelView>>> HandleAsync(ListModelsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        ModelRole? role = null;
        if (query.Role is not null)
        {
            role = ModelRoleExtensions.Parse(query.Role);
            if (role is null)
            {
                problems.Add(new FieldError("role", "The role is champion, challenger or retired."));
            }
        }

        if (query.Limit is < 1 or > 200)
        {
            problems.Add(new FieldError("limit", "The limit is 1 to 200."));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<IReadOnlyList<ModelView>>(LearningErrors.Invalid(problems));
        }

        var models = await registry.ListAsync(role, query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<ModelView>>([.. models.Select(ModelView.Of)]);
    }
}
