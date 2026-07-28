using Comprexy.Application.Abstractions;
using Comprexy.Application.Services;

namespace Comprexy.Application.Tests.Services;

public class ConversationRequestGateTests
{
    [Fact]
    public async Task AcquireAsync_SameKeyExclusive_SerializesCallers()
    {
        var gate = new ConversationRequestGate();
        var startedSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;

        var first = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            startedSecond.SetResult();
            await releaseFirst.Task;
        });

        await startedSecond.Task;

        var second = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            secondEntered = true;
        });

        await Task.Delay(50);
        Assert.False(secondEntered);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task AcquireAsync_DifferentKeys_DoNotBlockEachOther()
    {
        var gate = new ConversationRequestGate();
        var bothHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var a = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            bothHeld.TrySetResult();
            await release.Task;
        });

        var b = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-b",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            bothHeld.TrySetResult();
            await release.Task;
        });

        var completed = await Task.WhenAny(bothHeld.Task, Task.Delay(1000));
        Assert.Same(bothHeld.Task, completed);

        release.SetResult();
        await Task.WhenAll(a, b);
    }

    [Fact]
    public async Task AcquireAsync_Exclusive_WaitsForInFlightExclusiveLease()
    {
        var gate = new ConversationRequestGate();
        var backgroundHoldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBackground = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var background = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-wait",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            backgroundHoldStarted.SetResult();
            await releaseBackground.Task;
        });

        await backgroundHoldStarted.Task;

        var chatEntered = false;
        var chat = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-wait",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            chatEntered = true;
        });

        await Task.Delay(50);
        Assert.False(chatEntered);

        releaseBackground.SetResult();
        await Task.WhenAll(background, chat);
        Assert.True(chatEntered);
    }

    [Fact]
    public async Task AcquireAsync_NonExclusiveKind_Throws()
    {
        var gate = new ConversationRequestGate();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                (ConversationGateLeaseKind)99,
                CancellationToken.None);
        });
    }
}
