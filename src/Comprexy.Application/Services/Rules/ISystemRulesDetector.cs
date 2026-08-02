namespace Comprexy.Application.Services.Rules;

public interface ISystemRulesDetector
{
    (string BaseSystem, IReadOnlyList<RuleBlock> Rules) Detect(string? systemContent);
}
