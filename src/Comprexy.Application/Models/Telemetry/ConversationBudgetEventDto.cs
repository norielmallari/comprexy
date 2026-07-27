namespace Comprexy.Application.Models.Telemetry;

public sealed class ConversationBudgetEventDto
{
    public Guid ConversationId { get; init; }

    public int? SoftBudgetFirstExceededAtTurn { get; init; }

    public int? HardBudgetFirstExceededAtTurn { get; init; }

    public int? TrimFirstTriggeredAtTurn { get; init; }

    public int? MaxActualPromptTokens { get; init; }

    public int? MaxActualPromptTokensTurn { get; init; }

    public int? PostTrimActualPromptTokens { get; init; }
}
