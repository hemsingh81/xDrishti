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

    /// <summary>The store folder of the filing files (P15-02; inside the container <c>/data/filings/files</c>), created 0700 by the ingest. A host that ingests sets it explicitly, as <see cref="InboxPath"/>.</summary>
    [Required]
    [MinLength(1)]
    public string FilesPath { get; set; } = "data/filings/files";

    /// <summary>Minutes between the exchange broadcast and the moment a filing counts as known (P15-02, D3). A deployment setting: it changes research results, so it is never owner-switchable.</summary>
    [Range(0, 60)]
    public int LagMinutes { get; set; } = 5;

    /// <summary>The cap on the store folder, in GB; reaching it stops the ingest (<c>disk_budget</c>). Nothing is ever deleted.</summary>
    [Range(1, 100)]
    public int MaxFilesGb { get; set; } = 5;

    /// <summary>The free space the volume must keep, in GB; below it the ingest stops (<c>disk_free</c>).</summary>
    [Range(5, 500)]
    public int MinFreeGb { get; set; } = 20;
}
