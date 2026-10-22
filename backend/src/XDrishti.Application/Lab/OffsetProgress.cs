namespace XDrishti.Application.Lab;

/// <summary>Adds the instruments done in the earlier runs of a stage to the count of the current one, so one stage reports one rising number.</summary>
internal sealed class OffsetProgress(IProgress<int> inner, int offset) : IProgress<int>
{
    public void Report(int value) => inner.Report(offset + value);
}
