using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P15-08: llm-1 is defined and valid, but nothing can extract it yet (P15-10, after protocol L).</summary>
public sealed class LlmFeatureCatalogTests
{
    [Fact]
    public void The_group_is_a_valid_extension_group_with_distinct_names_that_repeat_no_catalog_name()
    {
        LlmFeatureCatalog.Group.Problem().ShouldBeNull();
        LlmFeatureCatalog.Group.Version.ShouldBe("llm-1");
        LlmFeatureCatalog.Group.Id.ShouldBe("llm");
        LlmFeatureCatalog.Names.Count.ShouldBe(3);
    }

    [Fact]
    public void The_group_is_not_extractable_so_no_feature_row_can_be_written_for_it()
    {
        ExtensionCatalogs.GroupOf(LlmFeatureCatalog.Version).ShouldBeNull();
    }
}
