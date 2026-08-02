using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

/// <summary>
/// Distills native client tool results into compact IR observations for the model.
/// </summary>
public class ToolIrResultDistiller
{
    private const int MaxJsonStringUnwrapDepth = 2;

    private static readonly HashSet<string> EnvelopeAllowlistTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "type", "notice", "file", "error", "warning", "lines", "truncated"
    };

    private static readonly string[] SearchNoMatchSentinels =
        ["no matches", "no results", "no files"];

    private static readonly string[] SearchErrorSentinels =
        ["error:", "error"];

    private static readonly Regex FooterTotalRegex = new(
        @"of\s+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly ToolSchemaOptions _options;
    private readonly ToolIrFileBodyCache _fileCache;
    private readonly ToolIrResultShapeStore _shapeStore;
    private readonly IToolIrShapeLearnQueue _shapeLearnQueue;

    public ToolIrResultDistiller(
        IOptions<ToolSchemaOptions> options,
        ToolIrFileBodyCache fileCache,
        ToolIrResultShapeStore shapeStore,
        IToolIrShapeLearnQueue shapeLearnQueue)
    {
        _options = options.Value;
        _fileCache = fileCache;
        _shapeStore = shapeStore;
        _shapeLearnQueue = shapeLearnQueue;
    }

    /// <summary>
    /// Drops cached file bodies after a successful client edit/write so the next IR read refreshes.
    /// </summary>
    public int InvalidateCachedFile(Guid conversationId, string path) =>
        _fileCache.Invalidate(conversationId, path);

    public string Distill(
        Guid conversationId,
        ToolIrCallMapping mapping,
        string nativeContent)
    {
        return mapping.ComprexyToolName switch
        {
            ToolSchemaConstants.FileRangeToolName => DistillFileRange(conversationId, mapping, nativeContent),
            ToolSchemaConstants.FileManifestToolName => DistillFileManifest(conversationId, mapping, nativeContent),
            ToolSchemaConstants.FileSearchToolName => DistillFileSearch(mapping, nativeContent),
            ToolSchemaConstants.DirListToolName => DistillDirList(mapping, nativeContent),
            ToolSchemaConstants.ShellToolName => DistillShell(mapping, nativeContent),
            _ => DistillPassthrough(mapping, nativeContent)
        };
    }

    private string DistillPassthrough(ToolIrCallMapping mapping, string nativeContent)
    {
        var content = Truncate(
            UnwrapJsonEncodedText(nativeContent),
            _options.MaxPassthroughObservationChars,
            out var truncated);
        return JsonSerializer.Serialize(new
        {
            type = "passthrough",
            tool = mapping.ComprexyToolName,
            truncated,
            content
        });
    }

    private string DistillShell(ToolIrCallMapping mapping, string nativeContent)
    {
        var content = UnwrapJsonEncodedText(nativeContent);
        var truncatedContent = Truncate(content, _options.MaxShellObservationChars);
        var truncated = !string.Equals(truncatedContent, content, StringComparison.Ordinal);
        return JsonSerializer.Serialize(new
        {
            type = "shell",
            command = TryReadArg(mapping.IrArgumentsJson, "command"),
            truncated,
            content = truncatedContent
        });
    }

    private string DistillFileRange(Guid conversationId, ToolIrCallMapping mapping, string nativeContent)
    {
        var path = mapping.Path ?? "unknown";
        var extracted = ExtractFileBody(conversationId, mapping.ClientToolName, nativeContent);
        var requestedStart = mapping.StartLine ?? 1;
        var requestedEnd = mapping.EndLine;
        var isUnwindowedFirstRead = mapping.EndLine is null;
        var lineCap = isUnwindowedFirstRead ? _options.FirstReadMaxLines : _options.MaxRangeLines;
        var absoluteStart = extracted.FirstLineNumber ?? requestedStart;
        var (bodyWithoutFooter, strippedFooterTotal) = StripReadPaginationFooterWithTotal(extracted.Body);
        var footerTotal = strippedFooterTotal ?? extracted.FooterTotalLineCount;
        extracted = extracted with { Body = bodyWithoutFooter };

        var observationCapHit = false;
        var bodyStartedAtOne = absoluteStart <= 1;
        var bodyCompleteCandidate = bodyStartedAtOne && footerTotal is null;

        ToolIrCachedFileBody? cached = null;
        string text;
        bool truncated;
        int returnedStart;
        int returnedEnd;

        if (!bodyStartedAtOne)
        {
            // Windowed native Read: body is already the requested slice (line prefixes stripped).
            // Do not store it as a full-file cache entry — that would poison later absolute ranges.
            text = CapWindowLines(extracted.Body, lineCap, out truncated);
            observationCapHit = truncated;
            if (isUnwindowedFirstRead && text.Length > _options.FirstReadMaxChars)
            {
                text = Truncate(text, _options.FirstReadMaxChars, out _);
                truncated = true;
                observationCapHit = true;
            }

            returnedStart = absoluteStart;
            returnedEnd = absoluteStart + CountContentLines(text) - (string.IsNullOrEmpty(text) ? 0 : 1);
            if (string.IsNullOrEmpty(text))
            {
                returnedEnd = absoluteStart - 1;
            }
            else if (CountContentLines(text) > 0)
            {
                returnedEnd = absoluteStart + CountContentLines(text) - 1;
            }

            // Incomplete: never cache as complete.
            bodyCompleteCandidate = false;
        }
        else
        {
            // Cap hit on observation means the cached body was also cut — mark incomplete.
            var cacheBody = extracted.Body;
            if (isUnwindowedFirstRead)
            {
                // Cache the full native body; observation may still be capped below.
                cached = _fileCache.SetIfRicher(
                    conversationId,
                    path,
                    cacheBody,
                    bodyComplete: bodyCompleteCandidate && true,
                    totalLineCount: footerTotal ?? (bodyCompleteCandidate ? CountContentLines(cacheBody) : null));
            }
            else
            {
                // For windowed reads starting at 1: complete only when no footer and body not
                // observation-capped relative to the request. Completeness for cache uses footer + start.
                cached = _fileCache.SetIfRicher(
                    conversationId,
                    path,
                    cacheBody,
                    bodyComplete: bodyCompleteCandidate,
                    totalLineCount: footerTotal ?? (bodyCompleteCandidate ? CountContentLines(cacheBody) : null));
            }

            var sliceEnd = requestedEnd ?? int.MaxValue;
            if (!ToolIrFileBodyCache.TrySliceLines(
                    cached,
                    requestedStart,
                    sliceEnd,
                    lineCap,
                    out text,
                    out truncated))
            {
                text = CapWindowLines(extracted.Body, lineCap, out truncated);
                returnedStart = requestedStart;
                returnedEnd = string.IsNullOrEmpty(text)
                    ? requestedStart - 1
                    : requestedStart + CountContentLines(text) - 1;
            }
            else
            {
                returnedStart = requestedStart;
                returnedEnd = string.IsNullOrEmpty(text)
                    ? requestedStart - 1
                    : requestedStart + CountContentLines(text) - 1;
            }

            observationCapHit = truncated;
            if (isUnwindowedFirstRead && text.Length > _options.FirstReadMaxChars)
            {
                text = Truncate(text, _options.FirstReadMaxChars, out _);
                truncated = true;
                observationCapHit = true;
                returnedEnd = requestedStart + CountContentLines(text) - (string.IsNullOrEmpty(text) ? 0 : 1);
                if (!string.IsNullOrEmpty(text))
                {
                    returnedEnd = requestedStart + CountContentLines(text) - 1;
                }
            }
        }

        // body_complete is read off the entry SetIfRicher returned (may be a pre-existing richer entry).
        var bodyComplete = cached?.BodyComplete ?? false;
        var totalLineCount = cached?.TotalLineCount
                             ?? footerTotal
                             ?? (bodyComplete ? ToolIrFileBodyCache.ContentLineCount(cached!) : null);
        if (cached is null)
        {
            totalLineCount = footerTotal;
        }

        var requestFullyCovered = requestedEnd is null
            ? bodyComplete && !observationCapHit
            : returnedStart <= requestedStart &&
              returnedEnd >= requestedEnd.Value &&
              bodyComplete;
        // When EndLine is set, "complete" means returned span covers the request AND body is complete.
        // When EndLine is null (unwindowed), complete means body complete and observation not truncated.
        var complete = requestedEnd is null
            ? bodyComplete && !observationCapHit
            : returnedStart <= requestedStart &&
              returnedEnd >= requestedEnd.Value &&
              bodyComplete;

        int? nextStartLine = complete || returnedEnd < requestedStart
            ? null
            : returnedEnd + 1;
        if (!complete && returnedEnd >= requestedStart)
        {
            nextStartLine = returnedEnd + 1;
        }
        else if (complete)
        {
            nextStartLine = null;
        }

        var hash = cached?.ContentHash
                   ?? ToolIrFileBodyCache.BuildEntry(path, extracted.Body).ContentHash;
        var pathOut = cached?.Path ?? ToolIrFileBodyCache.NormalizePath(path);

        return JsonSerializer.Serialize(new
        {
            type = "file_range",
            path = pathOut,
            requested_start_line = requestedStart,
            requested_end_line = requestedEnd,
            returned_start_line = returnedStart,
            returned_end_line = Math.Max(returnedStart - 1, returnedEnd),
            start_line = returnedStart,
            end_line = Math.Max(returnedStart - 1, returnedEnd),
            body_complete = bodyComplete,
            complete,
            total_line_count = totalLineCount,
            next_start_line = nextStartLine,
            truncated = observationCapHit,
            content_hash = hash,
            content = text
        });
    }

    private string DistillFileManifest(Guid conversationId, ToolIrCallMapping mapping, string nativeContent)
    {
        var path = mapping.Path ?? "unknown";
        var extracted = ExtractFileBody(conversationId, mapping.ClientToolName, nativeContent);
        var (bodyWithoutFooter, strippedFooterTotal) = StripReadPaginationFooterWithTotal(extracted.Body);
        var footerTotal = strippedFooterTotal ?? extracted.FooterTotalLineCount;
        extracted = extracted with { Body = bodyWithoutFooter };
        var absoluteStart = extracted.FirstLineNumber ?? 1;
        if (absoluteStart > 1)
        {
            // Manifest needs a full-ish body; refuse to cache/poison from a windowed read.
            var ephemeral = ToolIrFileBodyCache.BuildEntry(path, extracted.Body, bodyComplete: false, totalLineCount: footerTotal);
            return BuildManifestFromCache(
                ephemeral,
                _options.MaxManifestImports,
                _options.MaxManifestSymbols,
                _options.MaxManifestImportChars);
        }

        var bodyComplete = footerTotal is null;
        var cached = _fileCache.SetIfRicher(
            conversationId,
            path,
            extracted.Body,
            bodyComplete,
            totalLineCount: footerTotal ?? (bodyComplete ? CountContentLines(extracted.Body) : null));
        return BuildManifestFromCache(
            cached,
            _options.MaxManifestImports,
            _options.MaxManifestSymbols,
            _options.MaxManifestImportChars);
    }

    private static string CapWindowLines(string body, int maxLines, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(body) || maxLines < 1)
        {
            return body;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var contentCount = body.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        if (contentCount <= maxLines)
        {
            return body.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        truncated = true;
        return string.Join('\n', lines.Take(maxLines));
    }

    public static string BuildManifestFromCache(ToolIrCachedFileBody cached) =>
        BuildManifestFromCache(cached, maxImports: 20, maxSymbols: 30, maxImportChars: 160);

    public static string BuildManifestFromCache(
        ToolIrCachedFileBody cached,
        int maxImports,
        int maxSymbols,
        int maxImportChars)
    {
        var language = GuessLanguage(cached.Path);
        var imports = ExtractImportHints(cached.Body, maxImports, maxImportChars, out var importsTruncated);
        var symbols = ExtractSymbolHints(cached.Body, maxSymbols, out var symbolsTruncated);
        return JsonSerializer.Serialize(new
        {
            type = "file_manifest",
            path = cached.Path,
            language,
            line_count = ToolIrFileBodyCache.ContentLineCount(cached),
            size_bytes = Encoding.UTF8.GetByteCount(cached.Body),
            content_hash = cached.ContentHash,
            body_complete = cached.BodyComplete,
            imports_truncated = importsTruncated,
            symbols_truncated = symbolsTruncated,
            imports,
            symbols
        });
    }

    private string DistillFileSearch(ToolIrCallMapping mapping, string nativeContent)
    {
        var result = ExtractSearchMatches(
            nativeContent,
            _options.MaxSearchMatches,
            _options.MaxSearchPreviewChars,
            _options.SearchSentinelMaxChars);
        return JsonSerializer.Serialize(new
        {
            type = "file_search",
            query = TryReadArg(mapping.IrArgumentsJson, "query"),
            path = mapping.Path,
            truncated = result.MatchesTruncated || result.PreviewTruncated,
            matches_truncated = result.MatchesTruncated,
            preview_truncated = result.PreviewTruncated,
            match_count = result.Matches.Count,
            total_match_count = result.TotalCount,
            status = result.Status,
            parse_mode = result.ParseMode,
            notice = result.Notice,
            matches = result.Matches
        });
    }

    private string DistillDirList(ToolIrCallMapping mapping, string nativeContent)
    {
        var entries = ExtractDirEntries(
            nativeContent,
            _options.MaxDirListEntries,
            out var truncated,
            out var totalEntryCount);
        return JsonSerializer.Serialize(new
        {
            type = "dir_list",
            path = mapping.Path,
            truncated,
            entry_count = entries.Count,
            total_entry_count = totalEntryCount,
            entries
        });
    }

    public ExtractedFileBody ExtractFileBody(
        Guid conversationId,
        string? clientToolName,
        string nativeContent)
    {
        if (string.IsNullOrEmpty(nativeContent))
        {
            return new ExtractedFileBody(string.Empty, null, false, null);
        }

        nativeContent = UnwrapJsonEncodedText(nativeContent);

        if (string.IsNullOrWhiteSpace(clientToolName))
        {
            return ExtractFileBodyHeuristic(nativeContent);
        }

        var (descriptor, confidence) = ToolIrResultShapeProbe.Classify(nativeContent);

        if (confidence == ToolIrShapeConfidence.Unambiguous)
        {
            var heuristic = ExtractFileBodyHeuristic(nativeContent);
            _shapeStore.RecordProbe(conversationId, clientToolName, descriptor);
            if (_shapeStore.ShouldSample(conversationId, clientToolName))
            {
                var features = ToolIrShapeSanitizer.Build(
                    nativeContent,
                    confidence,
                    heuristic,
                    _options.ResultShape.MaxSampleLines);
                if (features is not null)
                {
                    var outcome = _shapeStore.RecordSample(conversationId, clientToolName, features);
                    if (outcome.ShouldEnqueue)
                    {
                        _shapeLearnQueue.TryEnqueue(new ToolIrShapeLearnJob(
                            conversationId,
                            clientToolName,
                            GuessVirtualToolForClient(clientToolName),
                            outcome.Snapshot));
                    }
                }
            }

            return heuristic;
        }

        // Ambiguous: consult store; use attested descriptor when present.
        if (_shapeStore.TryGet(conversationId, clientToolName, out var stored) && stored is not null)
        {
            if (ToolIrResultShape.TryExtractBody(nativeContent, stored, out var body, out var firstLine))
            {
                var fromShape = StripReadLinePrefixes(body);
                if (firstLine is not null)
                {
                    fromShape = fromShape with { FirstLineNumber = firstLine };
                }

                RecordAmbiguousSample(conversationId, clientToolName, nativeContent);
                return fromShape;
            }

            _shapeStore.Demote(conversationId, clientToolName, "attestation_failed");
        }

        var fallback = ExtractFileBodyHeuristic(nativeContent);
        RecordAmbiguousSample(conversationId, clientToolName, nativeContent);
        return fallback;
    }

    private void RecordAmbiguousSample(Guid conversationId, string clientToolName, string nativeContent)
    {
        if (!_shapeStore.ShouldSample(conversationId, clientToolName))
        {
            return;
        }

        var features = ToolIrShapeSanitizer.Build(
            nativeContent,
            ToolIrShapeConfidence.Ambiguous,
            heuristicBody: null,
            _options.ResultShape.MaxSampleLines);
        if (features is null)
        {
            return;
        }

        var outcome = _shapeStore.RecordSample(conversationId, clientToolName, features);
        if (outcome.ShouldEnqueue)
        {
            _shapeLearnQueue.TryEnqueue(new ToolIrShapeLearnJob(
                conversationId,
                clientToolName,
                GuessVirtualToolForClient(clientToolName),
                outcome.Snapshot));
        }
    }

    private static string GuessVirtualToolForClient(string clientToolName) =>
        // Job metadata only; learner does not branch on this.
        clientToolName;

    private static ExtractedFileBody ExtractFileBodyHeuristic(string nativeContent)
    {
        var trimmed = nativeContent.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(nativeContent);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var name in new[] { "contents", "content", "text", "data", "result" })
                    {
                        if (document.RootElement.TryGetProperty(name, out var value) &&
                            value.ValueKind == JsonValueKind.String)
                        {
                            return StripReadLinePrefixes(value.GetString() ?? string.Empty);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // fall through — treat as tagged / raw text
            }
        }

        if (TryExtractTaggedContent(nativeContent, out var tagged))
        {
            return StripReadLinePrefixes(tagged);
        }

        return StripReadLinePrefixes(nativeContent);
    }

    /// <summary>
    /// Decodes a native tool result delivered as a JSON-encoded string (a bare JSON string literal),
    /// so downstream parsing sees real newlines instead of <c>\n</c> escapes. Unwraps at most
    /// <see cref="MaxJsonStringUnwrapDepth"/> levels and only when the trimmed payload both starts and
    /// ends with a quote; anything else is returned unchanged. A payload still encoded after the bound
    /// is passed through intact — the search, shell, and passthrough envelopes report any resulting cut
    /// via their <c>truncated</c> flag rather than dropping content silently.
    /// Trade-off: a result that is legitimately a bare JSON string literal (e.g. a file whose entire
    /// content is <c>"hello"</c>) loses its outer quotes. That shape is indistinguishable from a
    /// double-encoded result, and only quoting/escaping is affected.
    /// </summary>
    private static string UnwrapJsonEncodedText(string nativeContent)
    {
        var current = nativeContent;
        for (var depth = 0; depth < MaxJsonStringUnwrapDepth; depth++)
        {
            var candidate = current.Trim();
            if (candidate.Length < 2 || candidate[0] != '"' || candidate[^1] != '"')
            {
                return current;
            }

            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind != JsonValueKind.String)
                {
                    return current;
                }

                current = document.RootElement.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
                return current;
            }
        }

        return current;
    }

    /// <summary>
    /// Envelope-gated unwrap: only when prelude/trailer are allowlisted wrappers and the close tag
    /// is the last <c>&lt;/content&gt;</c>. Gate fail → treat as raw file text.
    /// </summary>
    public static bool TryExtractTaggedContent(string nativeContent, out string content)
    {
        content = string.Empty;
        const string open = "<content>";
        const string close = "</content>";
        var start = nativeContent.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        if (!IsAllowlistedEnvelopeSide(nativeContent.AsSpan(0, start)))
        {
            return false;
        }

        var contentStart = start + open.Length;
        var end = nativeContent.LastIndexOf(close, StringComparison.OrdinalIgnoreCase);
        if (end < contentStart)
        {
            return false;
        }

        var afterClose = end + close.Length;
        if (afterClose < nativeContent.Length &&
            !IsAllowlistedEnvelopeSide(nativeContent.AsSpan(afterClose)))
        {
            return false;
        }

        content = nativeContent[contentStart..end];
        if (content.StartsWith('\n'))
        {
            content = content[1..];
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="side"/> is only whitespace and/or complete top-level elements from
    /// the envelope allowlist (<c>path</c>, <c>type</c>, <c>notice</c>, <c>file</c>, <c>error</c>,
    /// <c>warning</c>, <c>lines</c>, <c>truncated</c>).
    /// </summary>
    internal static bool IsAllowlistedEnvelopeSide(ReadOnlySpan<char> side)
    {
        var i = 0;
        while (i < side.Length)
        {
            while (i < side.Length && char.IsWhiteSpace(side[i]))
            {
                i++;
            }

            if (i >= side.Length)
            {
                return true;
            }

            if (side[i] != '<')
            {
                return false;
            }

            i++;
            if (i < side.Length && side[i] == '/')
            {
                return false;
            }

            var nameStart = i;
            while (i < side.Length && (char.IsLetterOrDigit(side[i]) || side[i] is '_' or '-'))
            {
                i++;
            }

            if (i == nameStart)
            {
                return false;
            }

            var tagName = side[nameStart..i].ToString();
            if (!EnvelopeAllowlistTags.Contains(tagName))
            {
                return false;
            }

            while (i < side.Length && side[i] != '>')
            {
                i++;
            }

            if (i >= side.Length)
            {
                return false;
            }

            i++; // past '>'
            var closeOpen = "</" + tagName + ">";
            var remaining = side[i..];
            var closeIdx = remaining.ToString().IndexOf(closeOpen, StringComparison.OrdinalIgnoreCase);
            if (closeIdx < 0)
            {
                return false;
            }

            i += closeIdx + closeOpen.Length;
        }

        return true;
    }

    /// <summary>
    /// Strips Read-tool line prefixes (<c>12: </c> / <c>12|</c>). Leaves plain text unchanged.
    /// When prefixes are present, <see cref="ExtractedFileBody.FirstLineNumber"/> is the first absolute line.
    /// </summary>
    internal static ExtractedFileBody StripReadLinePrefixes(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return new ExtractedFileBody(string.Empty, null, false, null);
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var prefixed = 0;
        var stripped = new string[lines.Length];
        int? firstLineNumber = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryStripLinePrefix(line, out var rest, out var lineNumber))
            {
                prefixed++;
                stripped[i] = rest;
                firstLineNumber ??= lineNumber;
            }
            else
            {
                stripped[i] = line;
            }
        }

        // Only treat as numbered Read output when most non-empty lines carry prefixes.
        var nonEmpty = lines.Count(static l => l.Length > 0);
        if (nonEmpty == 0 || prefixed * 2 < nonEmpty)
        {
            var (plainBody, plainTotal) = StripReadPaginationFooterWithTotal(normalized);
            return new ExtractedFileBody(plainBody, null, false, plainTotal);
        }

        var (prefixedBody, footerTotal) = StripReadPaginationFooterWithTotal(string.Join('\n', stripped));
        return new ExtractedFileBody(prefixedBody, firstLineNumber, true, footerTotal);
    }

    /// <summary>
    /// Removes Cursor/Kilo Read pagination trailers such as
    /// <c>(Showing lines 1-80 of 267. Use offset=81 to continue.)</c>
    /// so they are not cached as file body lines.
    /// </summary>
    internal static string StripReadPaginationFooter(string body) =>
        StripReadPaginationFooterWithTotal(body).Body;

    internal static (string Body, int? TotalLineCount) StripReadPaginationFooterWithTotal(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return (body, null);
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        if (end == 0)
        {
            return (normalized, null);
        }

        var last = lines[end - 1].Trim();
        if (!IsReadPaginationFooter(last))
        {
            return (normalized, null);
        }

        int? total = null;
        var match = FooterTotalRegex.Match(last);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed) && parsed > 0)
        {
            total = parsed;
        }

        end--;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        var stripped = end == 0 ? string.Empty : string.Join('\n', lines.Take(end));
        return (stripped, total);
    }

    private static bool IsReadPaginationFooter(string line) =>
        line.Contains("Showing lines", StringComparison.OrdinalIgnoreCase) &&
        (line.Contains("Use offset=", StringComparison.OrdinalIgnoreCase) ||
         line.Contains("of ", StringComparison.OrdinalIgnoreCase));

    private static bool TryStripLinePrefix(string line, out string rest, out int lineNumber)
    {
        rest = line;
        lineNumber = 0;
        var i = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]))
        {
            i++;
        }

        if (i == 0 || i >= line.Length)
        {
            return false;
        }

        if (!int.TryParse(line.AsSpan(0, i), out lineNumber))
        {
            return false;
        }

        if (line[i] == ':')
        {
            i++;
            if (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            rest = line[i..];
            return true;
        }

        if (line[i] == '|')
        {
            i++;
            rest = line[i..];
            return true;
        }

        return false;
    }

    public readonly record struct ExtractedFileBody(
        string Body,
        int? FirstLineNumber,
        bool HadLinePrefixes,
        int? FooterTotalLineCount = null);

    private readonly record struct SearchMatchResult(
        List<object> Matches,
        int TotalCount,
        bool MatchesTruncated,
        bool PreviewTruncated,
        string? Status,
        string? ParseMode,
        string? Notice);

    private static SearchMatchResult ExtractSearchMatches(
        string nativeContent,
        int max,
        int maxPreviewChars,
        int sentinelMaxChars)
    {
        nativeContent = UnwrapJsonEncodedText(nativeContent);
        var matches = new List<object>();
        var total = 0;
        var matchesTruncated = false;
        var previewTruncated = false;
        try
        {
            using var document = JsonDocument.Parse(nativeContent);
            if (TryEnumerateMatches(document.RootElement, out var elements))
            {
                foreach (var element in elements)
                {
                    total++;
                    if (matches.Count >= max)
                    {
                        matchesTruncated = true;
                        continue;
                    }

                    var preview = Truncate(
                        TryGetString(element, "preview", "content", "text", "snippet") ?? element.GetRawText(),
                        maxPreviewChars,
                        out var cut);
                    previewTruncated |= cut;
                    matches.Add(new
                    {
                        path = TryGetString(element, "path", "file", "filename") ?? string.Empty,
                        line = TryGetInt(element, "line", "line_number", "lineNumber") ?? 0,
                        preview
                    });
                }

                return new SearchMatchResult(
                    matches,
                    total,
                    matchesTruncated,
                    previewTruncated,
                    Status: null,
                    ParseMode: "json",
                    Notice: null);
            }
        }
        catch (JsonException)
        {
            // plain text fallback
        }

        var normalized = nativeContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var pathLineCount = 0;
        var nonEmptyLines = new List<string>();
        foreach (var rawLine in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            nonEmptyLines.Add(rawLine.TrimEnd());
            if (TryParsePathLinePrefix(rawLine.TrimEnd(), out _, out _, out _))
            {
                pathLineCount++;
            }
        }

        // Sentinel honesty: no path:line: parse and short / sentinel-shaped first line.
        if (pathLineCount == 0 && nonEmptyLines.Count > 0)
        {
            var first = nonEmptyLines[0].Trim();
            var isShort = normalized.Length < sentinelMaxChars;
            var status = TryClassifySearchSentinel(first);
            if (status is not null && (isShort || status is not null))
            {
                // Locked rule: if no path:line: AND (payload shorter than cap OR first line is sentinel)
                if (isShort || status is not null)
                {
                    var notice = Truncate(first, sentinelMaxChars);
                    return new SearchMatchResult(
                        [],
                        TotalCount: 0,
                        MatchesTruncated: false,
                        PreviewTruncated: false,
                        Status: status,
                        ParseMode: "sentinel",
                        Notice: notice);
                }
            }
        }

        foreach (var line in nonEmptyLines)
        {
            total++;
            if (matches.Count >= max)
            {
                matchesTruncated = true;
                continue;
            }

            if (TryParsePathLinePrefix(line, out var path, out var lineNumber, out var text))
            {
                var preview = Truncate(text, maxPreviewChars, out var cut);
                previewTruncated |= cut;
                matches.Add(new { path, line = lineNumber, preview });
                continue;
            }

            var fallbackPreview = Truncate(line, maxPreviewChars, out var fallbackCut);
            previewTruncated |= fallbackCut;
            matches.Add(new { path = "", line = 0, preview = fallbackPreview });
        }

        var parseMode = pathLineCount > 0 ? "path_line" : "unstructured";
        return new SearchMatchResult(
            matches,
            total,
            matchesTruncated,
            previewTruncated,
            Status: null,
            ParseMode: parseMode,
            Notice: null);
    }

    private static string? TryClassifySearchSentinel(string firstLineTrimmed)
    {
        foreach (var token in SearchNoMatchSentinels)
        {
            if (firstLineTrimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return "no_matches";
            }
        }

        foreach (var token in SearchErrorSentinels)
        {
            if (firstLineTrimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return "error";
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a grep-family search line (<c>path:line: text</c> or <c>path:line:text</c>) into its parts.
    /// Returns false for any line that does not carry a <c>:digits:</c> separator, so the caller can
    /// emit it as a preview-only match rather than dropping it.
    /// </summary>
    private static bool TryParsePathLinePrefix(string line, out string path, out int lineNumber, out string text)
    {
        path = string.Empty;
        lineNumber = 0;
        text = string.Empty;

        // Skip a Windows drive-letter colon so "C:/ws/a.cs:12: x" separates on the line colon.
        var scanStart = line.Length >= 3 &&
                        char.IsAsciiLetter(line[0]) &&
                        line[1] == ':' &&
                        (line[2] == '/' || line[2] == '\\')
            ? 2
            : 0;

        for (var c = scanStart; c < line.Length; c++)
        {
            if (line[c] != ':')
            {
                continue;
            }

            var d = c + 1;
            while (d < line.Length && char.IsAsciiDigit(line[d]))
            {
                d++;
            }

            var digits = d - (c + 1);
            if (digits == 0 || d >= line.Length || line[d] != ':')
            {
                continue;
            }

            if (c == 0 || digits > 9 || !int.TryParse(line.AsSpan(c + 1, digits), out var parsed) || parsed < 1)
            {
                return false;
            }

            path = ToolIrFileBodyCache.NormalizePath(line[..c]);
            lineNumber = parsed;
            text = line[(d + 1)..];
            if (text.StartsWith(' '))
            {
                text = text[1..];
            }

            return true;
        }

        return false;
    }

    private static List<object> ExtractDirEntries(
        string nativeContent,
        int max,
        out bool truncated,
        out int totalEntryCount)
    {
        nativeContent = UnwrapJsonEncodedText(nativeContent);
        truncated = false;
        totalEntryCount = 0;
        var entries = new List<object>();
        try
        {
            using var document = JsonDocument.Parse(nativeContent);
            var root = document.RootElement;
            JsonElement array = default;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "entries", "files", "items", "children", "result" })
                {
                    if (root.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                    {
                        array = candidate;
                        break;
                    }
                }
            }

            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    totalEntryCount++;
                    if (entries.Count >= max)
                    {
                        truncated = true;
                        continue;
                    }

                    if (element.ValueKind == JsonValueKind.String)
                    {
                        entries.Add(new { name = element.GetString(), kind = "unknown" });
                        continue;
                    }

                    entries.Add(new
                    {
                        name = TryGetString(element, "name", "path", "filename") ?? element.GetRawText(),
                        kind = TryGetString(element, "kind", "type", "entry_type") ?? "unknown"
                    });
                }

                return entries;
            }
        }
        catch (JsonException)
        {
            // plain text fallback
        }

        foreach (var line in nativeContent.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalEntryCount++;
            if (entries.Count >= max)
            {
                truncated = true;
                continue;
            }

            entries.Add(new { name = line.Trim(), kind = "unknown" });
        }

        return entries;
    }

    private static bool TryEnumerateMatches(JsonElement root, out IEnumerable<JsonElement> elements)
    {
        elements = [];
        if (root.ValueKind == JsonValueKind.Array)
        {
            elements = root.EnumerateArray();
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in new[] { "matches", "results", "hits", "items" })
        {
            if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                elements = array.EnumerateArray();
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractImportHints(
        string body,
        int max,
        int maxChars,
        out bool truncated)
    {
        truncated = false;
        var hints = new List<string>();
        var seen = 0;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
                trimmed.StartsWith("import ", StringComparison.Ordinal) ||
                trimmed.StartsWith("from ", StringComparison.Ordinal) ||
                trimmed.StartsWith("#include ", StringComparison.Ordinal))
            {
                seen++;
                if (hints.Count >= max)
                {
                    truncated = true;
                    continue;
                }

                hints.Add(Truncate(trimmed, maxChars));
            }
        }

        if (seen > max)
        {
            truncated = true;
        }

        return hints;
    }

    private static List<object> ExtractSymbolHints(string body, int max, out bool truncated)
    {
        truncated = false;
        var symbols = new List<object>();
        var lineNumber = 0;
        var seen = 0;
        foreach (var line in body.Split('\n'))
        {
            lineNumber++;
            var trimmed = line.TrimStart();
            if (LooksLikeSymbol(trimmed, out var name, out var kind))
            {
                seen++;
                if (symbols.Count >= max)
                {
                    truncated = true;
                    continue;
                }

                symbols.Add(new { name, kind, line = lineNumber });
            }
        }

        if (seen > max)
        {
            truncated = true;
        }

        return symbols;
    }

    private static bool LooksLikeSymbol(string trimmed, out string name, out string kind)
    {
        name = string.Empty;
        kind = "symbol";
        if (trimmed.StartsWith("class ", StringComparison.Ordinal) ||
            trimmed.StartsWith("public class ", StringComparison.Ordinal) ||
            trimmed.StartsWith("internal class ", StringComparison.Ordinal))
        {
            kind = "class";
            name = ExtractIdentifierAfter(trimmed, "class");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.StartsWith("interface ", StringComparison.Ordinal) ||
            trimmed.StartsWith("public interface ", StringComparison.Ordinal))
        {
            kind = "interface";
            name = ExtractIdentifierAfter(trimmed, "interface");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.StartsWith("function ", StringComparison.Ordinal) ||
            trimmed.StartsWith("def ", StringComparison.Ordinal) ||
            trimmed.StartsWith("fn ", StringComparison.Ordinal))
        {
            kind = "function";
            name = ExtractIdentifierAfter(trimmed, trimmed.StartsWith("function ", StringComparison.Ordinal) ? "function" : trimmed.StartsWith("def ", StringComparison.Ordinal) ? "def" : "fn");
            return !string.IsNullOrWhiteSpace(name);
        }

        if (trimmed.Contains("(", StringComparison.Ordinal) &&
            (trimmed.StartsWith("public ", StringComparison.Ordinal) ||
             trimmed.StartsWith("private ", StringComparison.Ordinal) ||
             trimmed.StartsWith("internal ", StringComparison.Ordinal) ||
             trimmed.StartsWith("protected ", StringComparison.Ordinal) ||
             trimmed.StartsWith("export ", StringComparison.Ordinal) ||
             trimmed.StartsWith("async ", StringComparison.Ordinal)))
        {
            kind = "function";
            var paren = trimmed.IndexOf('(');
            var before = trimmed[..paren].Trim();
            var parts = before.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            name = parts.Length > 0 ? parts[^1] : string.Empty;
            return !string.IsNullOrWhiteSpace(name);
        }

        return false;
    }

    private static string ExtractIdentifierAfter(string line, string keyword)
    {
        var idx = line.IndexOf(keyword, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        var rest = line[(idx + keyword.Length)..].TrimStart();
        var end = 0;
        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] is '_' or '$'))
        {
            end++;
        }

        return rest[..end];
    }

    private static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".js" => "javascript",
            ".jsx" => "jsx",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".md" => "markdown",
            ".json" => "json",
            ".yml" or ".yaml" => "yaml",
            _ => string.IsNullOrEmpty(ext) ? "unknown" : ext.TrimStart('.')
        };
    }

    private static int CountContentLines(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return 0;
        }

        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var count = 1;
        for (var i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '\n')
            {
                count++;
            }
        }

        return normalized[^1] == '\n' ? count - 1 : count;
    }

    private static string? TryReadArg(string argumentsJson, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
            {
                return n;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out n))
            {
                return n;
            }
        }

        return null;
    }

    private static string Truncate(string text, int maxChars) => Truncate(text, maxChars, out _);

    private static string Truncate(string text, int maxChars, out bool cut)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            cut = false;
            return text;
        }

        cut = true;
        return text[..maxChars] + "…";
    }
}
