using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The model trainer behind a port (P6-07, ADR 0070): a candidate for the validation harness and the final model with its bytes. The ML.NET adapter implements it in Infrastructure; no ML.NET type is visible here.</summary>
public interface IModelTrainer
{
    /// <summary>The candidate the harness judges (the same settings as <see cref="Train"/>).</summary>
    IScorerFactory Candidate { get; }

    /// <summary>The trainer settings as text, kept with the model.</summary>
    string Parameters { get; }

    /// <summary>Trains the final model on the rows, or says why it cannot.</summary>
    Result<TrainedModel> Train(IReadOnlyList<ValidationRow> rows);
}
