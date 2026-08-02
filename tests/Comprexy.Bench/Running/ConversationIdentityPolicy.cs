using System.ClientModel.Primitives;

namespace Comprexy.Bench.Running;

/// <summary>
/// Pins <c>X-Comprexy-Conversation-Id</c> on every request from one agent so the bench never
/// depends on fingerprint identity (two arms replay identical prompts and would collide).
///
/// What is sent is the conversation <em>key</em>; Comprexy stores it as <c>header:{value}</c> and
/// keys metrics by its own entity id, which it echoes back on the response. Reporting joins on
/// that echoed id, so the policy captures it here.
/// </summary>
internal sealed class ConversationIdentityPolicy(Guid conversationKey) : PipelinePolicy
{
    private const string HeaderName = "X-Comprexy-Conversation-Id";

    private Guid? _resolvedConversationId;

    /// <summary>Entity id the proxy reported, or null if no response carried the header.</summary>
    public Guid? ResolvedConversationId => _resolvedConversationId;

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ApplyRequestHeader(message);
        ProcessNext(message, pipeline, currentIndex);
        CaptureResponseHeader(message);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ApplyRequestHeader(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
        CaptureResponseHeader(message);
    }

    private void ApplyRequestHeader(PipelineMessage message) =>
        message.Request?.Headers.Set(HeaderName, conversationKey.ToString());

    private void CaptureResponseHeader(PipelineMessage message)
    {
        if (message.Response?.Headers.TryGetValue(HeaderName, out var value) == true &&
            Guid.TryParse(value, out var conversationId))
        {
            _resolvedConversationId = conversationId;
        }
    }
}
