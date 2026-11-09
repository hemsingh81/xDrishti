using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.Abstractions.SettingsCatalog;

/// <summary>The settings catalog as the host knows it: every descriptor with its current value and where that value comes from (<c>default</c>, <c>configuration</c>, <c>stored</c> or <c>section</c>). Never returns a secret.</summary>
public interface ISettingsCatalogSource
{
    /// <summary>Reads the entries.</summary>
    IReadOnlyList<SettingsCatalogEntry> Read();
}

/// <summary>A descriptor with its current value (null when it is stored state read on its own screen, or a section) and the source of that value.</summary>
public sealed record SettingsCatalogEntry(SettingDescriptor Descriptor, string? Value, string Source);
