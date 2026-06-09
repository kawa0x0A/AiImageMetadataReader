using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiImageMetadataReader;

internal static partial class MetadataBuilder
{
    internal static AiImageMetadata Build(
        Dictionary<string, string> entries,
        C2paMetadata? c2pa = null,
        int? imageWidth = null,
        int? imageHeight = null)
    {
        // AUTOMATIC1111 / WebUI
        if (entries.TryGetValue("parameters", out var sdParams))
        {
            var (prompt, negative, parameters) = ParseA1111Parameters(sdParams);
            return new AiImageMetadata
            {
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                StableDiffusionParameters = ParseStableDiffusionParameters(parameters),
                Software = AiSoftware.StableDiffusionWebUi,
                Prompt = prompt,
                NegativePrompt = negative,
                Parameters = parameters,
                C2pa = c2pa,
                RawEntries = entries,
            };
        }

        // NovelAI
        if ((entries.ContainsKey("Comment") && entries.ContainsKey("Source")) || entries.ContainsKey("Description"))
        {
            entries.TryGetValue("Description", out var novelPrompt);
            entries.TryGetValue("Comment", out var novelComment);
            var novelAiComment = TryParseNovelAiComment(novelComment);
            return new AiImageMetadata
            {
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                Software = AiSoftware.NovelAi,
                Prompt = novelPrompt,
                NegativePrompt = novelAiComment?.NegativePrompt,
                Parameters = novelComment,
                NovelAiComment = novelAiComment,
                C2pa = c2pa,
                RawEntries = entries,
            };
        }

        // ComfyUI
        if (entries.ContainsKey("workflow") || entries.ContainsKey("prompt"))
        {
            entries.TryGetValue("prompt", out var comfyPrompt);
            return new AiImageMetadata
            {
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                Software = AiSoftware.ComfyUi,
                Parameters = comfyPrompt,
                C2pa = c2pa,
                RawEntries = entries,
            };
        }

        // C2PA のみ（ChatGPT / DALL-E など）
        if (c2pa is not null)
        {
            return new AiImageMetadata
            {
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                Software = AiSoftware.C2paCompatible,
                C2pa = c2pa,
                RawEntries = entries,
            };
        }

        return new AiImageMetadata
        {
            ImageWidth = imageWidth,
            ImageHeight = imageHeight,
            RawEntries = entries,
        };
    }

    internal static (string? Prompt, string? Negative, string? Parameters) ParseA1111Parameters(string raw)
    {
        const string negativePrefix = "Negative prompt:";

        var lines = raw.ReplaceLineEndings("\n").Split('\n');

        int negativeLineIndex = -1;
        int parametersLineIndex = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (negativeLineIndex < 0 && lines[i].StartsWith(negativePrefix, StringComparison.Ordinal))
            {
                negativeLineIndex = i;
                continue;
            }

            if (negativeLineIndex >= 0 && lines[i].StartsWith("Steps:", StringComparison.Ordinal))
            {
                parametersLineIndex = i;
                break;
            }
        }

        if (negativeLineIndex < 0)
        {
            var lastNewline = raw.LastIndexOf('\n');
            if (lastNewline < 0)
                return (raw.Trim(), null, null);
            return (raw[..lastNewline].Trim(), null, raw[(lastNewline + 1)..].Trim());
        }

        var prompt = string.Join('\n', lines[..negativeLineIndex]).Trim();

        if (parametersLineIndex < 0)
        {
            var negative = string.Join('\n', lines[negativeLineIndex..])[negativePrefix.Length..].Trim();
            return (prompt, negative, null);
        }

        var negativePrompt = string.Join('\n', lines[negativeLineIndex..parametersLineIndex])[negativePrefix.Length..].Trim();
        var parameters = string.Join('\n', lines[parametersLineIndex..]).Trim();

        return (prompt, negativePrompt, parameters);
    }

    private static StableDiffusionParameters? ParseStableDiffusionParameters(string? parameters)
    {
        if (parameters is null) return null;

        // "Key: Value, Key: Value, ..." 形式をパース
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ParameterFieldRegex().Matches(parameters))
            fields[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();

        return new StableDiffusionParameters
        {
            Steps = TryGetInt(fields, "Steps"),
            Sampler = fields.GetValueOrDefault("Sampler"),
            ScheduleType = fields.GetValueOrDefault("Schedule type"),
            CfgScale = TryGetDouble(fields, "CFG scale"),
            Seed = TryGetLong(fields, "Seed"),
            Model = fields.GetValueOrDefault("Model"),
            ModelHash = fields.GetValueOrDefault("Model hash"),
            ClipSkip = TryGetInt(fields, "Clip skip"),
            Emphasis = fields.GetValueOrDefault("Emphasis"),
            Version = fields.GetValueOrDefault("Version"),
        };

        static int? TryGetInt(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : null;

        static long? TryGetLong(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) && long.TryParse(v, out var r) ? r : null;

        static double? TryGetDouble(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private static NovelAiComment? TryParseNovelAiComment(string? commentJson)
    {
        if (commentJson is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(commentJson);
            var root = doc.RootElement;

            return new NovelAiComment
            {
                NegativePrompt = root.TryGetProperty("uc", out var uc) ? uc.GetString() : null,
                Sampler = root.TryGetProperty("sampler", out var sampler) ? sampler.GetString() : null,
                Steps = root.TryGetProperty("steps", out var steps) && steps.TryGetInt32(out var stepsVal) ? stepsVal : null,
                CfgScale = root.TryGetProperty("scale", out var scale) && scale.TryGetDouble(out var scaleVal) ? scaleVal : null,
                Seed = root.TryGetProperty("seed", out var seed) && seed.TryGetInt64(out var seedVal) ? seedVal : null,
                Model = root.TryGetProperty("model", out var model) ? model.GetString() : null,
                Width = root.TryGetProperty("width", out var width) && width.TryGetInt32(out var widthVal) ? widthVal : null,
                Height = root.TryGetProperty("height", out var height) && height.TryGetInt32(out var heightVal) ? heightVal : null,
            };
        }
        catch { }
        return null;
    }

    // "Key: Value" のペアを抽出（Value はカンマの前まで、ただし次の "Word:" が来るまで）
    [GeneratedRegex(@"([\w ]+):\s*([^,]+?)(?=,\s*[\w ]+:|$)")]
    private static partial Regex ParameterFieldRegex();
}
