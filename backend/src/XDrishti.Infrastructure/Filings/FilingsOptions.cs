using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Filings;

/// <summary>The settings <c>Filings:*</c> of the folder-drop filings source (P15-01, ADR 0179), bounds checked at start-up. There is no key that fetches anything.</summary>
internal sealed class FilingsOptions
{
    public const string SectionName = "Filings";

    /// <summary>The inbox folder (inside the container <c>/data/filings/inbox</c>). Created 0700 by <c>scripts/filings-drop.sh</c>; the adapter never creates, writes or changes it.</summary>
    [Required]
    [MinLength(1)]
    public string InboxPath { get; set; } = "data/filings/inbox";

    /// <summary>The largest file accepted, in MiB.</summary>
    [Range(1, 200)]
    public int MaxFileMb { get; set; } = 20;

    /// <summary>The most files examined per scan; more is reported as truncated.</summary>
    [Range(100, 50_000)]
    public int MaxFiles { get; set; } = 5_000;
}
