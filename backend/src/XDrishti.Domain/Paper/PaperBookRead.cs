namespace XDrishti.Domain.Paper;

/// <summary>What reading the paper book for a plan gives: a <see cref="PaperBook"/> or <see cref="PaperBookUnavailable"/> (spec P10-02, criterion 8). Closed to these two.</summary>
public abstract record PaperBookRead
{
    private protected PaperBookRead()
    {
    }
}
