using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

public sealed record ToolIrCachedFileBody(
    string Path,
    string Body,
    string ContentHash,
    IReadOnlyList<int> LineStartOffsets);

/// <summary>
/// Process-local file-body cache keyed by conversationId + path. Owns a private MemoryCache.
/// </summary>
public sealed class ToolIrFileBodyCache : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ToolSchemaOptions _options;
    private readonly ConcurrentDictionary<string, object> _locks = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _pathsByConversation = new();
    private bool _disposed;

    public ToolIrFileBodyCache(IOptions<ToolSchemaOptions> options)
    {
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = Math.Max(1, _options.FileCacheSizeLimit)
        });
    }

    public bool TryGet(Guid conversationId, string path, out ToolIrCachedFileBody? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        entry = null;
        if (_cache.TryGetValue(BuildKey(conversationId, path), out ToolIrCachedFileBody? cached) && cached is not null)
        {
            entry = cached;
            return true;
        }

        return false;
    }

    public ToolIrCachedFileBody Set(Guid conversationId, string path, string body) =>
        SetCore(conversationId, path, body, replaceShorter: true);

    /// <summary>
    /// Caches a file body, but will not replace an existing entry that has more content lines
    /// (guards against partial Read windows overwriting a fuller cache).
    /// </summary>
    public ToolIrCachedFileBody SetIfRicher(Guid conversationId, string path, string body) =>
        SetCore(conversationId, path, body, replaceShorter: false);

    private ToolIrCachedFileBody SetCore(Guid conversationId, string path, string body, bool replaceShorter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedPath = NormalizePath(path);
        var key = BuildKey(conversationId, normalizedPath);
        var lockObj = _locks.GetOrAdd(key, static _ => new object());
        try
        {
            lock (lockObj)
            {
                var entry = BuildEntry(normalizedPath, body);
                if (!replaceShorter &&
                    _cache.TryGetValue(key, out ToolIrCachedFileBody? existing) &&
                    existing is not null &&
                    ContentLineCount(existing) > ContentLineCount(entry))
                {
                    return existing;
                }

                _cache.Set(
                    key,
                    entry,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _options.FileCacheAbsoluteExpiration,
                        Size = 1
                    });
                _pathsByConversation
                    .GetOrAdd(conversationId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase))
                    [normalizedPath] = 0;
                return entry;
            }
        }
        finally
        {
            if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, lockObj))
            {
                _locks.TryRemove(KeyValuePair.Create(key, lockObj));
            }
        }
    }

    /// <summary>
    /// True when the cache holds enough lines to answer an absolute <paramref name="startLine"/>..<paramref name="endLine"/> range.
    /// </summary>
    public bool TryGetCovering(
        Guid conversationId,
        string path,
        int startLine,
        int endLine,
        out ToolIrCachedFileBody? entry)
    {
        entry = null;
        if (!TryGet(conversationId, path, out var cached) || cached is null)
        {
            return false;
        }

        if (!CoversRange(cached, startLine, endLine))
        {
            return false;
        }

        entry = cached;
        return true;
    }

    public static bool CoversRange(ToolIrCachedFileBody entry, int startLine, int endLine)
    {
        if (startLine < 1 || endLine < startLine)
        {
            return false;
        }

        var lines = ContentLineCount(entry);
        if (lines == 0)
        {
            // Empty file only covers an explicit read of line 1 (empty observation).
            return startLine == 1;
        }

        // Require the full requested window to be present. A truncated native Read
        // (e.g. lines 1-80 of 267) must miss for start_line=80/end_line=180 so we
        // rematerialize — never locally "satisfy" with the short tail + pagination footer.
        return endLine <= lines;
    }

    /// <summary>Number of content lines (trailing newline does not add an extra empty line).</summary>
    public static int ContentLineCount(ToolIrCachedFileBody entry)
    {
        if (entry.Body.Length == 0)
        {
            return 0;
        }

        var count = entry.LineStartOffsets.Count;
        return entry.Body[^1] == '\n' ? count - 1 : count;
    }

    /// <summary>
    /// Drops cached bodies for <paramref name="path"/> and path aliases (absolute vs relative)
    /// so the next IR file read misses and refreshes from a native client Read.
    /// </summary>
    public int Invalidate(Guid conversationId, string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = NormalizePath(path);
        if (normalized.Length == 0)
        {
            return 0;
        }

        if (!_pathsByConversation.TryGetValue(conversationId, out var paths))
        {
            var exactKey = BuildKey(conversationId, normalized);
            if (!_cache.TryGetValue(exactKey, out _))
            {
                return 0;
            }

            _cache.Remove(exactKey);
            return 1;
        }

        var removed = 0;
        foreach (var cachedPath in paths.Keys)
        {
            if (!PathsMatch(cachedPath, normalized))
            {
                continue;
            }

            var key = BuildKey(conversationId, cachedPath);
            if (_cache.TryGetValue(key, out _))
            {
                _cache.Remove(key);
                removed++;
            }

            paths.TryRemove(cachedPath, out _);
        }

        return removed;
    }

    public static bool PathsMatch(string left, string right)
    {
        var a = NormalizePath(left);
        var b = NormalizePath(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return a.EndsWith('/' + b, StringComparison.OrdinalIgnoreCase) ||
               b.EndsWith('/' + a, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePath(string path) => path.Trim().Replace('\\', '/');

    public static ToolIrCachedFileBody BuildEntry(string path, string body)
    {
        var normalized = NormalizePath(path);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var offsets = BuildLineStartOffsets(body);
        return new ToolIrCachedFileBody(normalized, body, hash, offsets);
    }

    public static IReadOnlyList<int> BuildLineStartOffsets(string body)
    {
        var offsets = new List<int> { 0 };
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\n')
            {
                offsets.Add(i + 1);
            }
        }

        return offsets;
    }

    public static string SliceLines(ToolIrCachedFileBody entry, int startLine, int endLine, int maxLines, out bool truncated)
    {
        if (!TrySliceLines(entry, startLine, endLine, maxLines, out var text, out truncated))
        {
            truncated = false;
            return string.Empty;
        }

        return text;
    }

    /// <summary>
    /// Slices an absolute line range. Returns false when the range starts past the cached body
    /// (caller should treat as cache miss — never invent empty success for out-of-range).
    /// </summary>
    public static bool TrySliceLines(
        ToolIrCachedFileBody entry,
        int startLine,
        int endLine,
        int maxLines,
        out string text,
        out bool truncated)
    {
        text = string.Empty;
        truncated = false;
        var contentLines = ContentLineCount(entry);
        if (contentLines == 0)
        {
            return startLine == 1;
        }

        if (startLine < 1 || startLine > contentLines)
        {
            return false;
        }

        var start = startLine;
        var end = Math.Min(endLine, contentLines);
        if (endLine > contentLines)
        {
            truncated = true;
        }

        if (end - start + 1 > maxLines)
        {
            end = start + maxLines - 1;
            truncated = true;
        }

        var startOffset = entry.LineStartOffsets[start - 1];
        // Prefer the start of the next content line; trailing newline adds an extra offset.
        var endOffset = end < entry.LineStartOffsets.Count
            ? entry.LineStartOffsets[end]
            : entry.Body.Length;

        if (endOffset > startOffset && entry.Body[endOffset - 1] == '\n')
        {
            endOffset--;
        }

        text = entry.Body[startOffset..endOffset];
        return true;
    }

    private static string BuildKey(Guid conversationId, string path) =>
        $"toolir:file:{conversationId:N}:{NormalizePath(path)}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
        _pathsByConversation.Clear();
    }
}
