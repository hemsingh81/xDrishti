namespace XDrishti.Learning.MlNet;

/// <summary>
/// The settings of the ML.NET trainer (P6-06, ADR 0068). One fixed, conservative setting, no grid: with about two years of trades a grid searched inside the train folds is one more way to fit noise (the number of settings tried is the <c>trials</c> of the eligibility rule, here 1).
/// Threads are 1 and the seed is fixed so that the same rows give the same model bytes.
/// </summary>
/// <param name="Preferred">The trainer to try first; LightGBM falls back to FastTree when its native library cannot load.</param>
/// <param name="Trees">Boosting rounds.</param>
/// <param name="Leaves">Leaves per tree.</param>
/// <param name="LearningRate">Shrinkage per round.</param>
/// <param name="MinLeafRows">Fewest rows in a leaf.</param>
/// <param name="Seed">The seed of the trainer and of ML.NET.</param>
/// <param name="CalibrationShare">The share of the train rows (the latest by decision time) held back to fit the calibration map.</param>
/// <param name="MinFitRows">Fewest rows the trees are fitted on.</param>
/// <param name="MinCalibrationRows">Fewest rows the calibration is fitted on.</param>
/// <param name="ProbabilityFloor">The calibrated probability is kept within [floor, 1 - floor], so that a certain wrong answer cannot have an infinite loss.</param>
public sealed record MlNetTrainerOptions(
    TreeTrainer Preferred = TreeTrainer.LightGbm,
    int Trees = 100,
    int Leaves = 8,
    double LearningRate = 0.05,
    int MinLeafRows = 20,
    int Seed = 1,
    double CalibrationShare = 0.25,
    int MinFitRows = 40,
    int MinCalibrationRows = 15,
    double ProbabilityFloor = 0.005)
{
    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Trees is < 1 or > 5000)
        {
            problems.Add("trees must be 1 to 5000");
        }

        if (Leaves is < 2 or > 256)
        {
            problems.Add("leaves must be 2 to 256");
        }

        if (!double.IsFinite(LearningRate) || LearningRate is <= 0 or > 1)
        {
            problems.Add("learning_rate must be above 0 and at most 1");
        }

        if (MinLeafRows is < 1 or > 100000)
        {
            problems.Add("min_leaf_rows must be 1 to 100000");
        }

        if (!double.IsFinite(CalibrationShare) || CalibrationShare is <= 0.05 or >= 0.5)
        {
            problems.Add("calibration_share must be above 0.05 and below 0.5");
        }

        if (MinFitRows < 2 || MinCalibrationRows < 2)
        {
            problems.Add("min_fit_rows and min_calibration_rows must be at least 2");
        }

        if (!double.IsFinite(ProbabilityFloor) || ProbabilityFloor is < 0 or >= 0.5)
        {
            problems.Add("probability_floor must be 0 to below 0.5");
        }

        return problems;
    }
}
