using System.ComponentModel.DataAnnotations;
using XDrishti.Infrastructure.Ai.Llm;

namespace XDrishti.Infrastructure.Tests.Ai;

/// <summary>Spec P9-01 AC 12 and the options table: defaults OFF, ranges, base address, the key never in <c>ToString</c>, the pinned files.</summary>
public sealed class LlmOptionsTests
{
    private static bool Valid(LlmOptions o) => Validator.TryValidateObject(o, new ValidationContext(o), [], validateAllProperties: true);

    [Fact]
    public void The_defaults_are_off_valid_and_point_at_the_service_name()
    {
        var o = new LlmOptions();

        o.Enabled.ShouldBeFalse();
        Valid(o).ShouldBeTrue();
        o.HasValidBaseUrl().ShouldBeTrue();
        o.BaseUrl.ShouldBe("http://xd-llm:8080/v1");
        (o.RequestTimeoutSeconds, o.HealthTimeoutSeconds, o.ReadyWaitSeconds, o.StatusCacheSeconds).ShouldBe((180, 3, 300, 5));
    }

    [Theory]
    [InlineData(nameof(LlmOptions.RequestTimeoutSeconds), 9)]
    [InlineData(nameof(LlmOptions.RequestTimeoutSeconds), 601)]
    [InlineData(nameof(LlmOptions.HealthTimeoutSeconds), 0)]
    [InlineData(nameof(LlmOptions.HealthTimeoutSeconds), 11)]
    [InlineData(nameof(LlmOptions.ReadyWaitSeconds), 29)]
    [InlineData(nameof(LlmOptions.ReadyWaitSeconds), 901)]
    public void Out_of_range_values_are_refused(string property, int value)
    {
        var o = new LlmOptions();
        typeof(LlmOptions).GetProperty(property)!.SetValue(o, value);

        Valid(o).ShouldBeFalse();
    }

    [Theory]
    [InlineData("xd-llm:8080/v1", false)]
    [InlineData("ftp://xd-llm/v1", false)]
    [InlineData("https://host/v1", true)]
    public void The_base_address_is_an_absolute_http_address(string url, bool valid) => new LlmOptions { BaseUrl = url }.HasValidBaseUrl().ShouldBe(valid);

    [Fact]
    public void The_key_is_not_in_to_string()
    {
        var o = new LlmOptions { ApiKey = "super-secret-key-value" };

        o.ToString().ShouldNotContain("super-secret-key-value");
        $"{o}".ShouldNotContain("super-secret");
    }

    [Fact]
    public void The_known_models_hold_the_pinned_files_of_the_download_scripts()
    {
        var known = new LlmOptions().KnownModels;
        var script = File.ReadAllText(FindUp("scripts", "llm-models.sh"));

        foreach (var (file, sha) in known.Where(k => k.Key != "Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf"))
        {
            script.ShouldContain(file + "|");
            script.ShouldContain(sha);
        }

        File.ReadAllText(FindUp("scripts", "fetch-model.sh")).ShouldContain(known["Qwen3-30B-A3B-Instruct-2507-Q4_K_M.gguf"]);
    }

    private static string FindUp(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine([d.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
