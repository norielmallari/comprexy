using System.Text.Json;
using System.Text.Json.Serialization;
using Comprexy.Application.Configuration;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>Deterministic promote gate for idle shape-learner proposals.</summary>
public static class ToolIrShapeProposalValidator
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool Validate(
        string proposalJson,
        IReadOnlyList<ToolIrShapeFeatures> samples,
        ResultShapeOptions options,
        out ToolIrResultShape? descriptor,
        out string reason)
    {
        descriptor = null;
        reason = string.Empty;

        ToolIrResultShape? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ToolIrResultShape>(proposalJson, ParseOptions);
        }
        catch (JsonException)
        {
            reason = "not_closed_set";
            return false;
        }

        if (parsed is null || !Enum.IsDefined(parsed.Envelope) || !Enum.IsDefined(parsed.LinePrefix))
        {
            reason = "not_closed_set";
            return false;
        }

        if (parsed.Envelope == ToolIrEnvelopeKind.JsonField)
        {
            if (parsed.JsonField is null || !Enum.IsDefined(parsed.JsonField.Value))
            {
                reason = "not_closed_set";
                return false;
            }
        }
        else
        {
            parsed.JsonField = null;
        }

        // Reject free-text / unknown members by re-serializing known shape only — already closed enums.
        if (samples.Count < options.MinSamplesBeforeProposal)
        {
            reason = "sample_floor";
            return false;
        }

        var anchors = samples.Where(s => s.ObservedBody is not null).ToList();
        if (anchors.Count == 0)
        {
            reason = "no_anchor_sample";
            return false;
        }

        foreach (var anchor in anchors)
        {
            if (!ToolIrResultShape.TryReplaySpan(anchor, parsed, out var span, out var replayReason))
            {
                reason = replayReason == "not_attested" || replayReason == "prefix_disagrees_with_features"
                    ? "replay_mismatch"
                    : replayReason;
                return false;
            }

            var expected = anchor.ObservedBody!.Value;
            if (span.Start != expected.Start ||
                span.Length != expected.Length ||
                span.FirstLineNumber != expected.FirstLineNumber ||
                span.Prefix != expected.Prefix)
            {
                reason = "replay_mismatch";
                return false;
            }
        }

        var ambiguousResolved = false;
        foreach (var sample in samples.Where(s => s.ObservedBody is null))
        {
            if (ToolIrResultShape.TryReplaySpan(sample, parsed, out var span, out _) &&
                span.Length > 0)
            {
                ambiguousResolved = true;
                break;
            }
        }

        if (!ambiguousResolved)
        {
            reason = "no_ambiguity_resolved";
            return false;
        }

        parsed.Source = ToolIrShapeSource.Learner;
        parsed.Samples = samples.Count;
        parsed.ObservedAt = DateTimeOffset.UtcNow;
        descriptor = parsed;
        reason = string.Empty;
        return true;
    }
}
