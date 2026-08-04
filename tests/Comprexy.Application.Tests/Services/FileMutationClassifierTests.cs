using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class FileMutationClassifierTests
{
    [Theory]
    [InlineData("StrReplace", true)]
    [InlineData("Write", true)]
    [InlineData("write", true)]
    [InlineData("edit", true)]
    [InlineData("search_replace", true)]
    [InlineData("Read", false)]
    [InlineData("Shell", false)]
    public void IsMutatingFileTool_ClassifiesNames(string name, bool expected) =>
        Assert.Equal(expected, FileMutationClassifier.IsMutatingFileTool(name));

    [Fact]
    public void LooksLikeFailedFileMutation_DetectsStrReplaceMiss() =>
        Assert.True(FileMutationClassifier.LooksLikeFailedFileMutation(
            "Error: The string to replace was not found in the file (even after relaxing whitespace)."));

    [Fact]
    public void LooksLikeFailedFileMutation_DetectsKiloOldStringMiss() =>
        Assert.True(FileMutationClassifier.LooksLikeFailedFileMutation(
            "Could not find oldString in the file. It must match exactly, including whitespace, indentation, and line endings."));

    [Fact]
    public void LooksLikeSuccessfulFileMutation_RejectsFailures() =>
        Assert.False(FileMutationClassifier.LooksLikeSuccessfulFileMutation(
            "Error: The string to replace was not found in the file."));

    [Fact]
    public void LooksLikeSuccessfulFileMutation_DetectsUpdated() =>
        Assert.True(FileMutationClassifier.LooksLikeSuccessfulFileMutation(
            "The file /workspace/repo/a.ts has been updated."));

    [Fact]
    public void LooksLikeSuccessfulFileMutation_DetectsKiloWroteFile() =>
        Assert.True(FileMutationClassifier.LooksLikeSuccessfulFileMutation(
            "Wrote file successfully."));

    [Theory]
    [InlineData("Wrote file successfully.", true)]
    [InlineData("Edit applied successfully.", true)]
    [InlineData("ok", true)]
    [InlineData("", true)]
    [InlineData("Could not find oldString in the file.", false)]
    [InlineData("Error: The string to replace was not found in the file.", false)]
    public void ShouldInvalidateFileCache_InvalidatesUnlessFailed(string content, bool expected) =>
        Assert.Equal(expected, FileMutationClassifier.ShouldInvalidateFileCache(content));

    [Fact]
    public void TryExtractPathAndOldString_FromArgs()
    {
        const string args = """{"path":"/workspace/repo/a.ts","old_string":"foo","new_string":"bar"}""";
        Assert.Equal("/workspace/repo/a.ts", FileMutationClassifier.TryExtractPathFromToolArguments(args));
        Assert.Equal("foo", FileMutationClassifier.TryExtractOldStringFromToolArguments(args));
    }
}
