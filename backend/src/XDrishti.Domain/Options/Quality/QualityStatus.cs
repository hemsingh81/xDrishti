namespace XDrishti.Domain.Options.Quality;

/// <summary>The verdict of one check of the option archive (spec P14-04 criterion 1). <see cref="Unavailable"/> means the check could not be made: it never counts as <see cref="Pass"/> anywhere.</summary>
public enum QualityStatus
{
    Pass,
    Warn,
    Fail,
    Unavailable,
}
