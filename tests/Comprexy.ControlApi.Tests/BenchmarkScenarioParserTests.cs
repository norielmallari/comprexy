using Comprexy.ControlApi.Benchmarking;

namespace Comprexy.ControlApi.Tests;

public sealed class BenchmarkScenarioParserTests
{
  [Fact]
  public void Parse_LargestFilesScript_UsesCountAsPromptCount()
  {
    var path = WriteTempScript(
      "smoke-fixture.json",
      """
      {
        "provenance": "fixture largest-files script",
        "largestFiles": { "count": 10 },
        "promptTemplate": "fixture {{relativePath}}"
      }
      """);

    var scenario = BenchmarkScenarioParser.Parse(path);

    Assert.Equal("smoke-fixture", scenario.Name);
    Assert.Equal(10, scenario.PromptCount);
    Assert.Equal("fixture largest-files script", scenario.Description);
    Assert.True(scenario.IsSmoke);
  }

  [Fact]
  public void Parse_PromptArrayScript_CountsPrompts()
  {
    var path = WriteTempScript(
      "fixture-prompt-array.json",
      """
      {
        "description": "fixture prompt array",
        "prompts": ["one", "two", "three"]
      }
      """);

    var scenario = BenchmarkScenarioParser.Parse(path);

    Assert.Equal("fixture-prompt-array", scenario.Name);
    Assert.Equal(3, scenario.PromptCount);
    Assert.Equal("fixture prompt array", scenario.Description);
    Assert.False(scenario.IsSmoke);
  }

  [Fact]
  public void IsSmokeOnlyRun_RequiresAllSelectedScenariosToBeSmoke()
  {
    Assert.True(BenchmarkScenarioParser.IsSmokeOnlyRun(["smoke-large-blob"]));
    Assert.False(BenchmarkScenarioParser.IsSmokeOnlyRun(["smoke-large-blob", "short-deep"]));
    Assert.False(BenchmarkScenarioParser.IsSmokeOnlyRun([]));
  }

  private static string WriteTempScript(string fileName, string json)
  {
    var directory = Path.Combine("/tmp", "fixture-bench-parser", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, fileName);
    File.WriteAllText(path, json);
    return path;
  }
}
