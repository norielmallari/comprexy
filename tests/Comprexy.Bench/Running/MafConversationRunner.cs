using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using Comprexy.Bench.Cli;
using Comprexy.Bench.Hosting;
using Comprexy.Bench.Model;
using Comprexy.Bench.Tools;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Comprexy.Bench.Running;

/// <summary>
/// Drives one conversation through MAF against one arm's proxy: real file and shell tools rooted
/// at a gitignored sandbox, client-side compaction configured identically on both arms.
/// </summary>
internal sealed class MafConversationRunner(BenchOptions options)
{
    public async Task<BenchConversationRun> RunAsync(
        BenchArm arm,
        ResolvedArmConfiguration resolvedConfiguration,
        ConversationScript script,
        string workspaceCommit,
        CancellationToken cancellationToken)
    {
        var model = options.Model
            ?? resolvedConfiguration.ProviderModel
            ?? throw new BenchUsageException(
                "No model to send: pass --model, or set Provider:Model for the proxy host.");

        var conversationKey = Guid.NewGuid();
        var workspace = await SandboxWorkspace.CreateAsync(
            options.RunDirectory, arm.Name, script.Name, workspaceCommit, cancellationToken);
        var tools = new SandboxTools(workspace, TimeSpan.FromSeconds(options.ShellTimeoutSeconds)).CreateTools();

        var compaction = arm.UsesClientCompaction
            ? new CompactionObserver(
                new ContextWindowCompactionStrategy(options.MaxContextWindowTokens, options.MaxOutputTokens))
            : null;

        var identity = new ConversationIdentityPolicy(conversationKey);
        var agent = CreateAgent(
            arm,
            identity,
            model,
            resolvedConfiguration.RequiredApiKey,
            script.SystemPrompt,
            tools,
            compaction);

        using var conversationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        conversationCts.CancelAfter(TimeSpan.FromSeconds(options.ConversationTimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        var completedPrompts = 0;
        var status = ConversationStatus.Completed;
        string? failureReason = null;

        try
        {
            var session = await agent.CreateSessionAsync(conversationCts.Token);
            foreach (var prompt in script.Prompts)
            {
                await agent.RunAsync(prompt, session, cancellationToken: conversationCts.Token);
                completedPrompts++;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Two different caps surface as cancellation. Only the conversation cap trips our own
            // token source; anything else came from the client's per-completion network timeout,
            // which means the provider stalled on one prompt rather than the run being long.
            if (conversationCts.IsCancellationRequested)
            {
                status = ConversationStatus.TimedOut;
                failureReason =
                    $"conversation exceeded the {options.ConversationTimeoutSeconds}s wall-clock cap after {completedPrompts} prompt(s)";
            }
            else
            {
                status = ConversationStatus.CompletionStalled;
                failureReason =
                    $"upstream did not answer prompt {completedPrompts + 1} of {script.Prompts.Count} within the " +
                    $"{options.CompletionTimeoutSeconds}s per-completion cap; the conversation was abandoned there";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status = ConversationStatus.Failed;
            failureReason = $"{ex.GetType().Name}: {ex.Message}";
        }

        stopwatch.Stop();

        // The clone is torn down either way, so the diff is the only surviving record of what the
        // agent actually did to the tree. Teardown ignores the run's token: a cancelled run must not
        // leave a multi-megabyte clone behind under the run directory.
        try
        {
            await SaveChangePatchAsync(arm, script, workspace, CancellationToken.None);
        }
        finally
        {
            await workspace.RemoveAsync(CancellationToken.None);
        }

        if (identity.ResolvedConversationId is null && status == ConversationStatus.Completed)
        {
            status = ConversationStatus.Failed;
            failureReason =
                "the proxy never returned X-Comprexy-Conversation-Id, so this conversation cannot be joined to stored metrics";
        }

        return new BenchConversationRun(
            script.Name,
            conversationKey,
            identity.ResolvedConversationId,
            script.PromptListHash,
            script.Prompts.Count,
            completedPrompts,
            status,
            (long)stopwatch.Elapsed.TotalMilliseconds,
            compaction?.AppliedCount,
            failureReason);
    }

    private async Task SaveChangePatchAsync(
        BenchArm arm,
        ConversationScript script,
        SandboxWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var patch = await workspace.CaptureChangesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(patch))
        {
            return;
        }

        var directory = Path.Combine(options.RunDirectory, "workspace", arm.Name);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"{script.Name}.patch"), patch, cancellationToken);
    }

    private AIAgent CreateAgent(
        BenchArm arm,
        ConversationIdentityPolicy identity,
        string model,
        string? proxyApiKey,
        string instructions,
        IList<AITool> tools,
        CompactionStrategy? compaction)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{arm.BaseUrl}/v1"),
            NetworkTimeout = TimeSpan.FromSeconds(options.CompletionTimeoutSeconds)
        };
        clientOptions.AddPolicy(identity, PipelinePosition.PerCall);

        // Unless the host pins Auth:RequiredApiKey the proxy accepts anything, but System.ClientModel
        // still needs a non-empty credential.
        var credential = new ApiKeyCredential(
            string.IsNullOrWhiteSpace(proxyApiKey) ? "comprexy-bench" : proxyApiKey);
        var chatClient = new OpenAIClient(credential, clientOptions).GetChatClient(model);

        var agentOptions = new ChatClientAgentOptions
        {
            Name = $"comprexy-bench-{arm.Name}",
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
                Temperature = 0f,
                Seed = options.Seed,
                MaxOutputTokens = options.MaxOutputTokens
            },
            AIContextProviders = compaction is null ? [] : [new CompactionProvider(compaction)]
        };

        return chatClient.AsAIAgent(agentOptions);
    }
}
