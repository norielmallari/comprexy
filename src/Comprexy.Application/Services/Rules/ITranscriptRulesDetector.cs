using Comprexy.Application.Models;

namespace Comprexy.Application.Services.Rules;

public interface ITranscriptRulesDetector
{
    IReadOnlyList<RuleBlock> Detect(IReadOnlyList<ChatMessage> newClientMessages);
}
