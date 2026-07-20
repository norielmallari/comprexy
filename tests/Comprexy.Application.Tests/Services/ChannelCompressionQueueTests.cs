using Comprexy.Application.Models;
using Comprexy.Domain.Enums;
using Comprexy.Infrastructure.BackgroundJobs;

namespace Comprexy.Application.Tests.Services;

public class ChannelCompressionQueueTests
{
    [Fact]
    public async Task Enqueue_SameConversation_CoalescesWhilePending()
    {
        var queue = new ChannelCompressionQueue();
        var conversationId = Guid.NewGuid();

        queue.Enqueue(new CompressionJob(conversationId, CompressionMode.Background));
        queue.Enqueue(new CompressionJob(conversationId, CompressionMode.HighPriorityBackground));
        queue.Enqueue(new CompressionJob(conversationId, CompressionMode.Background));

        await using var enumerator = queue.DequeueAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(conversationId, enumerator.Current.ConversationId);
        Assert.Equal(CompressionMode.Background, enumerator.Current.Mode);

        var otherId = Guid.NewGuid();
        queue.Enqueue(new CompressionJob(otherId, CompressionMode.Background));
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(otherId, enumerator.Current.ConversationId);
    }

    [Fact]
    public async Task Enqueue_AfterDequeue_AllowsAnotherJobForSameConversation()
    {
        var queue = new ChannelCompressionQueue();
        var conversationId = Guid.NewGuid();

        queue.Enqueue(new CompressionJob(conversationId, CompressionMode.Background));

        await using var enumerator = queue.DequeueAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        queue.Enqueue(new CompressionJob(conversationId, CompressionMode.HighPriorityBackground));
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(CompressionMode.HighPriorityBackground, enumerator.Current.Mode);
    }
}
