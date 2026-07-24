using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Comprexy.Infrastructure.Hosting;

/// <summary>
/// Resolves a shared SQLite file under the repo <c>data/</c> directory so proxy and control-api
/// (both under <c>apps/*</c>) use the same database regardless of content root.
/// </summary>
public static class SharedSqliteConfiguration
{
    public static void UseRepoSharedDatabase(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var dataDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "comprexy.db");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Comprexy"] = $"Data Source={dbPath};Cache=Shared"
        });
    }
}
