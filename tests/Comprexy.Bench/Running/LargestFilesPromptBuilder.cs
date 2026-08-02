namespace Comprexy.Bench.Running;

internal static class LargestFilesPromptBuilder
{
    public const string RelativePathPlaceholder = "{{relativePath}}";
    public const string ContentsPlaceholder = "{{contents}}";

    public static List<string> Build(string scriptName, RepoLargestFilesSelector.Options options, string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException(
                $"Conversation script {scriptName} sets 'largestFiles' but 'promptTemplate' is missing or empty.");
        }

        if (!template.Contains(RelativePathPlaceholder, StringComparison.Ordinal) ||
            !template.Contains(ContentsPlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Conversation script {scriptName} promptTemplate must include {RelativePathPlaceholder} and {ContentsPlaceholder}.");
        }

        var files = RepoLargestFilesSelector.Select(options);
        var prompts = new List<string>(files.Count);
        foreach (var file in files)
        {
            prompts.Add(
                template
                    .Replace(RelativePathPlaceholder, file.RelativePath, StringComparison.Ordinal)
                    .Replace(ContentsPlaceholder, file.Contents, StringComparison.Ordinal));
        }

        return prompts;
    }
}
