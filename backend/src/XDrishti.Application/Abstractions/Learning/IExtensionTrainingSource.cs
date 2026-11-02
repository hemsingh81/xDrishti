using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where protocol X gets its rows (P15-10): the stored runs, their <c>tf-1</c> rows and the extension rows behind a port. Reads only.</summary>
public interface IExtensionTrainingSource
{
    /// <summary>Every stored run with its extraction record for the version, oldest key first.</summary>
    ValueTask<IReadOnlyList<ExtensionRunSource>> ListRunsAsync(string version, CancellationToken cancellationToken);

    /// <summary>The labelled rows of the runs with the extension appended.</summary>
    ValueTask<Result<ExtensionTrainingData>> LoadAsync(ExtensionGroup group, IReadOnlyList<string> runKeys, CancellationToken cancellationToken);
}
