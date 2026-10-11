using XDrishti.Application.Portfolio;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>The answers about a first import (ADR 0013) that come with saving the portfolio settings.</summary>
internal static class FirstImports
{
    /// <summary>The code the owner's screen shows when the settings were saved but the import was not.</summary>
    public const string FailedCode = "portfolio.import_failed";

    public static readonly FirstImportView Skipped = new("skipped", 0, 0, 0, 0, null, null, "The account already has holdings; nothing was imported. Later changes arrive with the refresh.");

    public static FirstImportView Deferred(string message) => new("deferred", 0, 0, 0, 0, null, null, message);

    public static FirstImportView Done(ImportResultView result) =>
        new("imported", result.Imported, result.DefaultDateLots, result.AlreadyImported, result.Unknown, null, null, null);

    public static FirstImportView Failed(Error cause) =>
        new("failed", 0, 0, 0, 0, FailedCode, cause.Code, "The settings were saved but the holdings were not imported: " + cause.Message + " Save again or press Sync now to retry.");
}
