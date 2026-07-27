namespace Comprexy.Application.Models.Telemetry;

public sealed class ConversationComparisonDto
{
    public ConversationSummaryDto Left { get; init; } = null!;

    public ConversationSummaryDto Right { get; init; } = null!;
}
