using XDrishti.Application.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Accounts;

/// <summary>The answers about a first import (ADR 0013) that come with saving the portfolio settings.</summary>
internal static class FirstImports
{
    /// <summary>The code the owner's screen shows when the settings were saved but the import was not.</summary>
    public const string FailedCode = "portfolio.import_failed";

    /// <summary>The account has holdings: nothing is imported; <paramref name="redating"/> says how many lots followed a changed first import date (ADR 0015).</summary>
    public static FirstImportView Skipped(LotRedating redating) =>
        new("skipped", 0, 0, 0, 0, redating.Redated, redating.Skipped, null, null, "The account already has holdings; nothing was imported. Later changes arrive with the refresh.");

    public static FirstImportView Deferred(string message) => new("deferred", 0, 0, 0, 0, 0, 0, null, null, message);

    public static FirstImportView Done(ImportResultView result) =>
        new("imported", result.Imported, result.DefaultDateLots, result.AlreadyImported, result.Unknown, 0, 0, null, null, null);

    public static FirstImportView Failed(Error cause) =>
        new("failed", 0, 0, 0, 0, 0, 0, FailedCode, cause.Code, "The settings were saved but the holdings were not imported: " + cause.Message + " Save again or press Sync now to retry.");
}
