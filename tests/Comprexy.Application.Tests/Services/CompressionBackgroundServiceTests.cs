using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Infrastructure.BackgroundJobs;

namespace Comprexy.Application.Tests.Services;

public class CompressionBackgroundServiceTests
{
    [Fact]
    public void ContextPolicyOptions_CancelBackgroundCompressionOnChat_DefaultsToFalse()
    {
        Assert.False(new ContextPolicyOptions().CancelBackgroundCompressionOnChat);
    }

    [Theory]
    [InlineData(true, ConversationGateLeaseKind.Preemptible)]
    [InlineData(false, ConversationGateLeaseKind.Exclusive)]
    public void ResolveSoftCompressionLeaseKind_MapsCancelFlag(
        bool cancelOnChat,
        ConversationGateLeaseKind expected)
    {
        Assert.Equal(
            expected,
            CompressionBackgroundService.ResolveSoftCompressionLeaseKind(cancelOnChat));
    }
}
