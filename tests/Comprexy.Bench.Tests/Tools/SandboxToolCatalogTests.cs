using System.Text.Json;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Bench.Tools;
using Comprexy.Infrastructure.Tokenization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Comprexy.Bench.Tests.Tools;

public class SandboxToolCatalogTests
{
    private const int BandMinInclusive = 14_500;
    private const int BandMaxInclusive = 16_500;
    private const int HardFailAbove = 17_500;

    [Fact]
    public void CreateTools_CompactOpenAiJson_IsInIdeTokenBand_AndWireShapeIsValid()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comprexy-bench-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SandboxWorkspace.CreateTransient(tempDir);
            var tools = SandboxToolCatalog.CreateTools(workspace, TimeSpan.FromSeconds(5));
            var compact = SandboxToolCatalog.ToCompactOpenAiToolsJson(tools);

            using var document = JsonDocument.Parse(compact);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Equal(tools.Count, document.RootElement.GetArrayLength());

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                Assert.Equal(JsonValueKind.Object, entry.ValueKind);
                Assert.Equal("function", entry.GetProperty("type").GetString());
                var function = entry.GetProperty("function");
                Assert.Equal(JsonValueKind.Object, function.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(function.GetProperty("name").GetString()));
                Assert.Equal(JsonValueKind.Object, function.GetProperty("parameters").ValueKind);
            }

            var estimator = new TiktokenTokenEstimator(
                Options.Create(new ContextPolicyOptions { TokenizerEncoding = "cl100k_base" }),
                new PassthroughTokenEstimateCache());
            var tokens = estimator.CountTokens(compact);

            Assert.True(
                tokens >= BandMinInclusive,
                $"Off tools[] compact cl100k tokens {tokens} is below IDE band floor {BandMinInclusive}.");
            Assert.True(
                tokens <= BandMaxInclusive,
                $"Off tools[] compact cl100k tokens {tokens} is above IDE band ceiling {BandMaxInclusive}.");
            Assert.True(
                tokens <= HardFailAbove,
                $"Off tools[] compact cl100k tokens {tokens} exceeds hard kitchen-sink ceiling {HardFailAbove}.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateTools_IncludesAllStockDenylistStubNames()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comprexy-bench-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tools = SandboxToolCatalog.CreateTools(
                SandboxWorkspace.CreateTransient(tempDir),
                TimeSpan.FromSeconds(5));
            var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

            Assert.Equal(10, SandboxToolCatalog.StockExcludeFromModelTools.Count);
            foreach (var excluded in SandboxToolCatalog.StockExcludeFromModelTools)
            {
                Assert.Contains(excluded, names);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateTools_WriteFileAndEditFile_DeclareSandboxTools()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comprexy-bench-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = SandboxWorkspace.CreateTransient(tempDir);
            var tools = SandboxToolCatalog.CreateTools(workspace, TimeSpan.FromSeconds(5));

            var write = Assert.IsAssignableFrom<AIFunction>(
                Assert.Single(tools, t => t.Name == "WriteFile"));
            var edit = Assert.IsAssignableFrom<AIFunction>(
                Assert.Single(tools, t => t.Name == "EditFile"));

            Assert.NotNull(write.UnderlyingMethod);
            Assert.NotNull(edit.UnderlyingMethod);
            Assert.Equal(typeof(SandboxTools), write.UnderlyingMethod!.DeclaringType);
            Assert.Equal(typeof(SandboxTools), edit.UnderlyingMethod!.DeclaringType);

            await write.InvokeAsync(
                new AIFunctionArguments
                {
                    ["path"] = "notes.txt",
                    ["content"] = "hello-from-catalog-test"
                });
            Assert.True(File.Exists(Path.Combine(tempDir, "notes.txt")));
            Assert.Equal("hello-from-catalog-test", File.ReadAllText(Path.Combine(tempDir, "notes.txt")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateTools_TaskPassthrough_IsPresentAndNotOnStockExcludeList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "comprexy-bench-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tools = SandboxToolCatalog.CreateTools(
                SandboxWorkspace.CreateTransient(tempDir),
                TimeSpan.FromSeconds(5));
            var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

            Assert.Contains("Task", names);
            Assert.DoesNotContain(
                "Task",
                SandboxToolCatalog.StockExcludeFromModelTools,
                StringComparer.OrdinalIgnoreCase);

            // Stock product exclude fixture copy (appsettings not edited; assert against the same names).
            string[] stockExcludeFixture =
            [
                "ReadLints",
                "TodoWrite",
                "AwaitShell",
                "UpdateCurrentStep",
                "EditNotebook",
                "SwitchMode",
                "agent_manager",
                "agent_manager_models",
                "background_process",
                "kilo_local_recall"
            ];
            Assert.Equal(
                SandboxToolCatalog.StockExcludeFromModelTools.OrderBy(x => x, StringComparer.Ordinal),
                stockExcludeFixture.OrderBy(x => x, StringComparer.Ordinal));
            Assert.DoesNotContain("Task", stockExcludeFixture, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("task", stockExcludeFixture, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CatalogVersion_IsIdeBandV1()
    {
        Assert.False(string.IsNullOrWhiteSpace(SandboxToolCatalog.CatalogVersion));
        Assert.Equal("ide-band-v1", SandboxToolCatalog.CatalogVersion);
    }

    private sealed class PassthroughTokenEstimateCache : ITokenEstimateCache
    {
        public int GetOrCompute(string key, Func<int> compute, CancellationToken cancellationToken = default) =>
            compute();
    }
}
