using System.Runtime.Versioning;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criterion 9 of P3-09: the writer's one new optional parameter, the folder name below the root. The default is <c>sim</c> (the tests of P3-04b1 pass unchanged); a pack goes to <c>verify</c>; anything that is not 1 to 20 lower-case letters is refused. Temp directories only.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class ExportFolderWriterFolderNameTests
{
    private static Task<XDrishti.Domain.Common.Result<ExportOutcome>> WriteAsync(string root, string? folder) =>
        folder is null
            ? new ExportFolderWriter().WriteAsync(root, ExportFixture.RunId, ExportFixture.Files(), ExportFixture.BuildManifest(), false, TestContext.Current.CancellationToken)
            : new ExportFolderWriter().WriteAsync(root, ExportFixture.RunId, ExportFixture.Files(), ExportFixture.BuildManifest(), false, TestContext.Current.CancellationToken, folder);

    [Fact]
    public async Task Without_the_parameter_the_run_goes_to_sim_and_with_verify_it_goes_to_verify_and_the_two_do_not_meet()
    {
        using var temp = new TempFolder();

        var sim = await WriteAsync(temp.Root, null);
        var verify = await WriteAsync(temp.Root, "verify");

        sim.IsSuccess.ShouldBeTrue();
        verify.IsSuccess.ShouldBeTrue(verify.IsFailure ? verify.Error.Message : string.Empty);
        sim.Value.Folder.ShouldBe(Path.Combine(temp.Root, "sim", ExportFixture.RunId));
        verify.Value.Folder.ShouldBe(Path.Combine(temp.Root, "verify", ExportFixture.RunId));
        File.GetUnixFileMode(Path.Combine(temp.Root, "verify")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        (await WriteAsync(temp.Root, "verify")).Value.Status.ShouldBe(ExportStatus.Unchanged);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("Verify")]
    [InlineData("ve rify")]
    [InlineData("verify1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz")]
    public async Task A_folder_name_that_is_not_1_to_20_lower_case_letters_is_refused_and_nothing_is_written(string folder)
    {
        using var temp = new TempFolder();

        var result = await WriteAsync(temp.Root, folder);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        Directory.Exists(temp.Root).ShouldBeFalse();
    }
}
