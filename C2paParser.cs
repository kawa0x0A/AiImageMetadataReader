using System.Text;
using System.Text.RegularExpressions;

namespace AiImageMetadataReader;

/// <summary>
/// PNG の caBX チャンク（JUMBF コンテナ内の C2PA CBOR データ）から
/// 文字列パターンマッチで必要な情報を抽出する。
/// </summary>
internal static partial class C2paParser
{
    private const string TrainedAlgorithmicMedia = "trainedAlgorithmicMedia";

    internal static C2paMetadata? Parse(byte[] data)
    {
        // CBOR バイナリから UTF-8 文字列を総当たりで抽出する
        var strings = ExtractUtf8Strings(data, minLength: 4);

        string? generatorName = null;
        string? generatorVersion = null;
        string? claimGenerator = null;
        DateTimeOffset? generatedAt = null;
        string? digitalSourceType = null;
        bool isAiGenerated = false;

        foreach (var s in strings)
        {
            // デジタルソース種別
            if (s.Contains("digitalsourcetype/", StringComparison.OrdinalIgnoreCase))
            {
                digitalSourceType = s;
                if (s.Contains(TrainedAlgorithmicMedia, StringComparison.OrdinalIgnoreCase))
                    isAiGenerated = true;
                continue;
            }

            // 生成日時（ISO 8601）
            if (generatedAt is null && Iso8601Regex().IsMatch(s))
            {
                if (DateTimeOffset.TryParse(s, out var dt))
                    generatedAt = dt;
                continue;
            }

            // ソフトウェアエージェント名（例: "gpt-image"）
            // CBOR の softwareAgent キーの直後に現れる短い識別子
            if (generatorName is null && IsSoftwareAgentName(s))
            {
                generatorName = s;
                continue;
            }

            // バージョン文字列（例: "2.0", "c2.0"）
            if (generatorVersion is null && generatorName is not null && IsVersionString(s))
            {
                generatorVersion = s.TrimStart('c');
                continue;
            }

            // クレームジェネレータ（OpenAI などの API 名）
            if (claimGenerator is null && IsClaimGeneratorString(s))
            {
                claimGenerator = s;
                continue;
            }
        }

        if (generatorName is null && claimGenerator is null && !isAiGenerated)
            return null;

        return new C2paMetadata
        {
            GeneratorName = generatorName,
            GeneratorVersion = generatorVersion,
            ClaimGenerator = claimGenerator,
            GeneratedAt = generatedAt,
            IsAiGenerated = isAiGenerated,
            DigitalSourceType = digitalSourceType,
        };
    }

    private static List<string> ExtractUtf8Strings(byte[] data, int minLength)
    {
        var results = new List<string>();
        var i = 0;

        while (i < data.Length)
        {
            // CBOR テキスト文字列: major type 3 (0x60–0x7b)
            byte b = data[i];
            int len;

            if (b >= 0x60 && b <= 0x77)
            {
                len = b - 0x60;
                i++;
            }
            else if (b == 0x78 && i + 1 < data.Length)
            {
                len = data[i + 1];
                i += 2;
            }
            else if (b == 0x79 && i + 2 < data.Length)
            {
                len = (data[i + 1] << 8) | data[i + 2];
                i += 3;
            }
            else
            {
                i++;
                continue;
            }

            if (len < minLength || i + len > data.Length)
            {
                i++;
                continue;
            }

            try
            {
                var s = Encoding.UTF8.GetString(data, i, len);
                if (IsPrintable(s))
                    results.Add(s);
                i += len;
            }
            catch
            {
                i++;
            }
        }

        return results;
    }

    private static bool IsPrintable(string s)
    {
        foreach (var c in s)
        {
            if (c < 0x20 && c != '\n' && c != '\r' && c != '\t')
                return false;
        }
        return true;
    }

    private static bool IsSoftwareAgentName(string s)
    {
        // 短くて英数字とハイフンのみ（例: "gpt-image", "stable-diffusion"）
        if (s.Length > 40 || s.Length < 3) return false;
        return SoftwareNameRegex().IsMatch(s);
    }

    private static bool IsVersionString(string s)
    {
        // "2.0", "c2.0", "1.0.0" など
        return VersionRegex().IsMatch(s);
    }

    private static bool IsClaimGeneratorString(string s)
    {
        // "OpenAI Media Service API" など、スペースを含む比較的長い名前
        if (s.Length < 5 || s.Length > 100) return false;
        return s.Contains("API", StringComparison.Ordinal)
            || s.Contains("Service", StringComparison.Ordinal)
            || s.Contains("Generator", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex Iso8601Regex();

    [GeneratedRegex(@"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$")]
    private static partial Regex SoftwareNameRegex();

    [GeneratedRegex(@"^c?\d+\.\d+(\.\d+)*$")]
    private static partial Regex VersionRegex();
}
