using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The trial of a comparison (P13-05, D6): what makes one look different from another, as text and hash. The caller appends the ledger row with this hash before the core computes anything.</summary>
public static class ExtensionTrial
{
    /// <summary>The family prefix of the ledger: <c>ext:&lt;version&gt;</c>.</summary>
    public const string FamilyPrefix = "ext:";

    /// <summary>The trial of <paramref name="group"/> against nothing, under this recipe and these options, on the data window <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static ExtensionTrialId Of(ExtensionGroup group, string recipeName, ExtensionComparisonOptions options, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(recipeName);
        ArgumentNullException.ThrowIfNull(options);
        var text = string.Join('\n', "extension-trial/1", "base:" + TradeFeatureCatalog.Version, "extension:" + group.Version + ":" + string.Join(',', group.Names), "recipe:" + recipeName + ":" + options.RecipeOptionsHash, "label:" + options.LabelRuleVersion, "options:" + options.Canonical());
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var window = string.Create(CultureInfo.InvariantCulture, $"ext-ledger/1\n{hash}\n{from:yyyy-MM-dd}\n{to:yyyy-MM-dd}");
        var runId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(window)).AsSpan(0, 16));
        return new ExtensionTrialId(FamilyPrefix + group.Version, text, hash, runId);
    }
}
