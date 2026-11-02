using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where protocol F gets its population (P15-10): the stored runs and the results calendar behind a port. Reads only.</summary>
public interface IFilterPopulationSource
{
    /// <summary>The population, or the error of the calendar read.</summary>
    ValueTask<Result<FilterPopulation>> ReadAsync(CancellationToken cancellationToken);
}
