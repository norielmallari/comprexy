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
    public async Task AcquireAsync_Exclusive_PreemptsPreemptibleLease()
    {
        var gate = new ConversationRequestGate();
        var backgroundStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundSawCancel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exclusiveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var background = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                ConversationGateLeaseKind.Preemptible,
                CancellationToken.None);
            backgroundStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, lease.Token);
            }
            catch (OperationCanceledException)
            {
                backgroundSawCancel.SetResult();
            }
        });

        await backgroundStarted.Task;

        var exclusive = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-a",
                ConversationGateLeaseKind.Exclusive,
                CancellationToken.None);
            exclusiveEntered.SetResult();
            await Task.Delay(20);
        });

        var canceled = await Task.WhenAny(backgroundSawCancel.Task, Task.Delay(2000));
        Assert.Same(backgroundSawCancel.Task, canceled);

        var entered = await Task.WhenAny(exclusiveEntered.Task, Task.Delay(2000));
        Assert.Same(exclusiveEntered.Task, entered);

        await Task.WhenAll(background, exclusive);
    }

    [Fact]
    public async Task AcquireAsync_Exclusive_DoesNotWaitOnPreemptibleWorkDuration()
    {
        var gate = new ConversationRequestGate();
        var backgroundHoldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var background = Task.Run(async () =>
        {
            await using var lease = await gate.AcquireAsync(
                "conv-preempt",
                ConversationGateLeaseKind.Preemptible,
                CancellationToken.None);
            backgroundHoldStarted.SetResult();
            try
            {
                // Simulate a long compression call that honors preempt.
                await Task.Delay(TimeSpan.FromSeconds(30), lease.Token);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        });

        await backgroundHoldStarted.Task;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using (var lease = await gate.AcquireAsync(
                         "conv-preempt",
                         ConversationGateLeaseKind.Exclusive,
                         CancellationToken.None))
        {
            sw.Stop();
        }

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Exclusive acquire took {sw.Elapsed}; should preempt quickly.");
        await background;
    }

    [Fact]
    public async Task AcquireAsync_Exclusive_WaitsForInFlightExclusiveLease()
    {
        // Mirrors CancelBackgroundCompressionOnChat=false: soft compression holds Exclusive.
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
}
