namespace XDrishti.Application.Learning;

/// <summary>A model with everything the registry keeps about it (P6-07).</summary>
/// <param name="Model">The model.</param>
/// <param name="Parameters">The trainer and validation settings.</param>
/// <param name="ReportText">The validation report with its honest limits.</param>
/// <param name="Calibration">The calibration table.</param>
/// <param name="Importance">The features by weight, largest first.</param>
/// <param name="Designations">Every designation, oldest first.</param>
/// <param name="Limits">The sentences that must be read with the model.</param>
public sealed record ModelDetailView(ModelView Model, string Parameters, string ReportText, IReadOnlyList<CalibrationPoint> Calibration, IReadOnlyList<FeatureImportanceView> Importance, IReadOnlyList<DesignationView> Designations, IReadOnlyList<string> Limits);
