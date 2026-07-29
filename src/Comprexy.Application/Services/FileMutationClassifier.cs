using System.Text.Json;

namespace Comprexy.Application.Services;

/// <summary>
/// Shared heuristics for client passthrough file-mutation tools (StrReplace / Write / …).
/// Used by Virtual Tools cache invalidation, live failed-edit dedupe, and Inline fold pin.
/// </summary>
public static class FileMutationClassifier
{
    private static readonly string[] PathPropertyNames =
    [
        "filePath",
        "file_path",
        "target_file",
        "path"
    ];

    private static readonly string[] OldStringPropertyNames =
    [
        "old_string",
        "oldString",
        "OldString"
    ];

    public static bool IsMutatingFileTool(string toolName) =>
        toolName.Equals("edit", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("write", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("StrReplace", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("Write", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("search_replace", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("ApplyPatch", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeSuccessfulFileMutation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (LooksLikeFailedFileMutation(content))
        {
            return false;
        }

        return content.Contains("Edit applied successfully", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Wrote contents", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Updated file", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("has been written", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("has been updated", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("occurrences in file", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeFailedFileMutation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("string to replace was not found", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryExtractPathFromToolArguments(string? argumentsJson)
    {
        return TryExtractStringProperty(argumentsJson, PathPropertyNames);
    }

    public static string? TryExtractOldStringFromToolArguments(string? argumentsJson)
    {
        return TryExtractStringProperty(argumentsJson, OldStringPropertyNames);
    }

    public static string NormalizePath(string path) => FileReadPathExtractor.Normalize(path);

    public static string NormalizeOldStringKey(string? oldString) =>
        string.IsNullOrEmpty(oldString) ? string.Empty : oldString.Replace("\r\n", "\n").Trim();

    private static string? TryExtractStringProperty(string? argumentsJson, string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in propertyNames)
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
