using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Learning;

/// <summary>Where the model files live (configuration section <c>Models</c>; spec P6-07, ADR 0070). Files are named by their SHA-256.</summary>
internal sealed class ModelStoreOptions
{
    public const string SectionName = "Models";

    /// <summary>The folder of the model files; relative paths are relative to the working directory, so the <c>xd</c> script sets an absolute one (<c>data/models</c> of the repository).</summary>
    [Required]
    public string Folder { get; set; } = "data/models";
}
