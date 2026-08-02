using System.Text.Json;
using System.Text.Json.Serialization;

namespace Comprexy.Bench;

internal static class BenchJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Options), cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name} from {path}.");
    }
}
