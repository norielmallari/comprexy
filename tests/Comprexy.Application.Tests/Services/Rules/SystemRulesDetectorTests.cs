using Comprexy.Application.Services.Rules;

namespace Comprexy.Application.Tests.Services.Rules;

public class SystemRulesDetectorTests
{
    private readonly SystemRulesDetector _detector = new();

    [Fact]
    public void Detect_KiloInstructionsFrom_ExtractsKeyedBodiesAndStripsBaseSystem()
    {
        var input = """
            You are a helpful assistant.

            Instructions from: /workspace/repo/.kilo/rules/rule-a.md
            Always use synthetic paths in tests.

            Instructions from: /workspace/repo/.kilo/rules/rule-b.md
            Second rule body.
            """;

        var (baseSystem, rules) = _detector.Detect(input);

        Assert.Contains("helpful assistant", baseSystem);
        Assert.DoesNotContain("Always use synthetic", baseSystem);
        Assert.Equal(2, rules.Count);
        Assert.Equal("rule-a.md", rules[0].NormalizedKey);
        Assert.Equal("Always use synthetic paths in tests.", rules[0].Body);
        Assert.Equal("rule-b.md", rules[1].NormalizedKey);
    }

    [Fact]
    public void Detect_CursorGlobPattern_ExtractsRuleBlock()
    {
        var input = """
            Persona preamble.

            glob pattern(s) for applicable files: **/*.cs
            --- rule: /workspace/repo/.cursor/rules/csharp.mdc ---
            Use explicit types in C#.
            """;

        var (baseSystem, rules) = _detector.Detect(input);

        Assert.Equal("Persona preamble.", baseSystem.Trim());
        Assert.Single(rules);
        Assert.Equal("csharp.mdc", rules[0].NormalizedKey);
        Assert.Equal("Use explicit types in C#.", rules[0].Body);
    }

    [Fact]
    public void Detect_AlwaysAppliedWorkspaceRules_ExtractsEmbeddedRules()
    {
        var input = """
            Base text.
            <always_applied_workspace_rules>
            --- rule: /workspace/repo/.cursor/rules/global.mdc ---
            Global standing rule.
            </always_applied_workspace_rules>
            """;

        var (baseSystem, rules) = _detector.Detect(input);

        Assert.Equal("Base text.", baseSystem.Trim());
        Assert.Single(rules);
        Assert.Equal("global.mdc", rules[0].NormalizedKey);
        Assert.Equal("Global standing rule.", rules[0].Body);
    }
}
