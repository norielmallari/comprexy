namespace Comprexy.Application.Services.Rules;

public enum RuleSource
{
    System,
    Transcript
}

public sealed record RuleBlock(
    string NormalizedKey,
    string Title,
    string Body,
    RuleSource Source);
