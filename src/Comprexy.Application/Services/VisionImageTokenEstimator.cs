namespace Comprexy.Application.Services;

/// <summary>
/// Approximates vision-model image token cost. OpenAI-compatible high/low detail tile math —
/// never BPE-tokenizes base64 payloads (that over-counts by orders of magnitude).
/// </summary>
public static class VisionImageTokenEstimator
{
    public const int LowDetailTokens = 85;
    public const int HighDetailBaseTokens = 85;
    public const int TokensPerTile = 170;

    /// <summary>
    /// When dimensions are unknown (http URL, unsupported codec), assume a mid-size high-detail
    /// image (~4 tiles) rather than tokenizing the URL/body.
    /// </summary>
    public const int UnknownDimensionFallbackTokens = 765;

    public static int Estimate(string? imageUrl, string? detail)
    {
        if (IsLowDetail(detail))
        {
            return LowDetailTokens;
        }

        if (TryGetDimensions(imageUrl, out var width, out var height))
        {
            return EstimateHighDetail(width, height);
        }

        return UnknownDimensionFallbackTokens;
    }

    public static int EstimateHighDetail(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return UnknownDimensionFallbackTokens;
        }

        // Match OpenAI vision resizing: fit within 2048², then shortest side to 768, then 512 tiles.
        double w = width;
        double h = height;
        var longest = Math.Max(w, h);
        if (longest > 2048)
        {
            var scale = 2048d / longest;
            w *= scale;
            h *= scale;
        }

        var shortest = Math.Min(w, h);
        if (shortest > 768)
        {
            var scale = 768d / shortest;
            w *= scale;
            h *= scale;
        }

        var tilesX = (int)Math.Ceiling(w / 512d);
        var tilesY = (int)Math.Ceiling(h / 512d);
        return HighDetailBaseTokens + TokensPerTile * tilesX * tilesY;
    }

    public static bool IsDataImageUrl(string? url) =>
        !string.IsNullOrEmpty(url) &&
        url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

    public static string RedactImageUrlForText(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "[image]";
        }

        if (IsDataImageUrl(url))
        {
            var mime = "image";
            var comma = url.IndexOf(',', StringComparison.Ordinal);
            var header = comma > 0 ? url[..comma] : url;
            var slash = header.IndexOf('/', StringComparison.Ordinal);
            var semi = header.IndexOf(';', StringComparison.Ordinal);
            if (slash >= 0)
            {
                var end = semi > slash ? semi : header.Length;
                if (end > slash + 1)
                {
                    mime = header[(slash + 1)..end];
                }
            }

            return $"[image: data:{mime};base64,…]";
        }

        return $"[image: {url}]";
    }

    public static bool TryGetDimensions(string? imageUrl, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!IsDataImageUrl(imageUrl))
        {
            return false;
        }

        var comma = imageUrl!.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0 || comma + 1 >= imageUrl.Length)
        {
            return false;
        }

        var header = imageUrl[..comma];
        var payload = imageUrl[(comma + 1)..];
        // Enough for PNG IHDR / JPEG SOF / WebP headers; avoid decoding the whole image.
        var prefixChars = Math.Min(payload.Length, 512);
        var prefix = payload.AsSpan(0, prefixChars);

        Span<byte> decoded = stackalloc byte[384];
        if (!TryDecodeBase64Prefix(prefix, decoded, out var bytesWritten) || bytesWritten < 24)
        {
            return false;
        }

        var bytes = decoded[..bytesWritten];
        if (header.Contains("png", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadPng(bytes, out width, out height);
        }

        if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
            header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadJpeg(bytes, out width, out height);
        }

        if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadWebp(bytes, out width, out height);
        }

        // Sniff when the data-URL subtype is missing/odd.
        return TryReadPng(bytes, out width, out height) ||
               TryReadJpeg(bytes, out width, out height) ||
               TryReadWebp(bytes, out width, out height);
    }

    private static bool IsLowDetail(string? detail) =>
        string.Equals(detail, "low", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeBase64Prefix(ReadOnlySpan<char> prefix, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        // Trim to a multiple of 4 for the decoder; ignore incomplete trailing quartet.
        var usable = prefix.Length - (prefix.Length % 4);
        if (usable < 4)
        {
            return false;
        }

        return Convert.TryFromBase64Chars(prefix[..usable], destination, out bytesWritten);
    }

    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        // Signature (8) + length (4) + "IHDR" (4) + width/height (8)
        if (data.Length < 24)
        {
            return false;
        }

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!data[..8].SequenceEqual(signature))
        {
            return false;
        }

        if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
        {
            return false;
        }

        width = ReadInt32BigEndian(data.Slice(16, 4));
        height = ReadInt32BigEndian(data.Slice(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                return false;
            }

            var segmentLength = (data[offset] << 8) | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                return false;
            }

            // SOF0..SOF3, SOF5..SOF7, SOF9..SOF11, SOF13..SOF15
            if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                height = (data[offset + 3] << 8) | data[offset + 4];
                width = (data[offset + 5] << 8) | data[offset + 6];
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool TryReadWebp(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30)
        {
            return false;
        }

        if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F')
        {
            return false;
        }

        if (data[8] != (byte)'W' || data[9] != (byte)'E' || data[10] != (byte)'B' || data[11] != (byte)'P')
        {
            return false;
        }

        // VP8X extended: bytes 24-26 width-1, 27-29 height-1 (24-bit LE)
        if (data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)'X')
        {
            width = 1 + (data[24] | (data[25] << 8) | (data[26] << 16));
            height = 1 + (data[27] | (data[28] << 8) | (data[29] << 16));
            return width > 0 && height > 0;
        }

        // VP8 lossy: starts with "VP8 " then frame header
        if (data[12] == (byte)'V' && data[13] == (byte)'P' && data[14] == (byte)'8' && data[15] == (byte)' ')
        {
            if (data.Length < 30)
            {
                return false;
            }

            // After chunk size (4 bytes at 16), keyframe start code 0x9d 0x01 0x2a then 14-bit width/height
            var frame = data[20..];
            if (frame.Length < 10 || frame[3] != 0x9D || frame[4] != 0x01 || frame[5] != 0x2A)
            {
                return false;
            }

            width = frame[6] | ((frame[7] & 0x3F) << 8);
            height = frame[8] | ((frame[9] & 0x3F) << 8);
            return width > 0 && height > 0;
        }

        return false;
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> span) =>
        (span[0] << 24) | (span[1] << 16) | (span[2] << 8) | span[3];
}
