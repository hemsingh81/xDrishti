namespace XDrishti.Domain.Tests.Assistant.Evaluation;

internal static class EvalTables
{
    public static ThresholdTable Thresholds()
    {
        var read = ThresholdTable.Parse(EvalFiles.Text("thresholds.json"));
        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Message : string.Empty);
        return read.Value;
    }
}
