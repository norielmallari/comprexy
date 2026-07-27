using Comprexy.Application.Models.Telemetry;

namespace Comprexy.Application.Abstractions;

public interface IEvidenceMarkdownService
{
    string Build(ConversationSummaryDto summary, FinalTurnSnapshotDto finalTurn);
}
