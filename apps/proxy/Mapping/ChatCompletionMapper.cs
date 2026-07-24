using Comprexy.Api.Contracts;
using Comprexy.Application.Models;

namespace Comprexy.Api.Mapping;

public static class ChatCompletionMapper
{
    public static ChatCompletionResponseDto ToResponseDto(ProxyChatCompletionResult result)
    {
        return new ChatCompletionResponseDto
        {
            Id = $"comprexy-{Guid.NewGuid():N}",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = result.Model,
            Choices =
            [
                new ChatCompletionChoiceDto
                {
                    Index = 0,
                    Message = new ChatMessageDto { Role = "assistant", Content = result.AssistantContent },
                    FinishReason = result.FinishReason ?? "stop"
                }
            ],
            Usage = new ChatCompletionUsageDto
            {
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                TotalTokens = result.PromptTokens + result.CompletionTokens
            }
        };
    }
}
