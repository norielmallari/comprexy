using Comprexy.Application.Models;

namespace Comprexy.Application.Services.Rules;

public interface IRulesInjector
{
    IReadOnlyList<ChatMessage> BuildPendingMessages(RulesSnapshot snapshot, bool hasWorkingMemory);
}
