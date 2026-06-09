# AiImageMetadataReader

A lightweight async library for reading AI-generated image metadata from PNG and JPEG files.

## Supported Tools

| Tool | Format | Prompt | Negative Prompt | Parameters |
|---|---|---|---|---|
| Stable Diffusion (AUTOMATIC1111 / WebUI) | PNG / JPEG | ✅ | ✅ | ✅ |
| NovelAI | PNG | ✅ | ✅ | ✅ (typed) |
| ComfyUI | PNG | - | - | ✅ (workflow JSON) |
| ChatGPT / DALL-E | PNG | - | - | ✅ (C2PA) |

## Installation

```
dotnet add package AiImageMetadataReader
```

## Usage

### Auto-detect format (PNG / JPEG)

```csharp
using AiImageMetadataReader;

var metadata = await AiMetadataReader.ReadAsync("image.png");

if (!metadata.IsSuccess)
{
    Console.WriteLine(metadata.Error);
    return;
}

Console.WriteLine(metadata.Software);        // StableDiffusionWebUi
Console.WriteLine(metadata.Prompt);         // masterpiece, best quality ...
Console.WriteLine(metadata.NegativePrompt); // lowres, bad anatomy ...
Console.WriteLine(metadata.Parameters);     // Steps: 20, CFG scale: 7 ...
```

### Read PNG directly

```csharp
var metadata = await AiImageMetadataReader.ReadAsync("image.png");
```

### Read JPEG directly

```csharp
var metadata = await JpegAiMetadataReader.ReadAsync("image.jpg");
```

### Non-seekable stream (with format hint)

```csharp
await using var stream = response.Content.ReadAsStreamAsync();
var metadata = await AiMetadataReader.ReadAsync(stream, hint: ".png");
```

### Batch processing

```csharp
await foreach (var (file, metadata) in AiMetadataReader.ReadDirectoryAsync("C:/images"))
{
    Console.WriteLine($"{Path.GetFileName(file)}: {metadata.Prompt}");
}

// Including subdirectories
await foreach (var (file, metadata) in AiMetadataReader.ReadDirectoryAsync(
    "C:/images", SearchOption.AllDirectories))
{
    // ...
}
```

### Stable Diffusion typed parameters

```csharp
var metadata = await AiMetadataReader.ReadAsync("sd.png");
var sd = metadata.StableDiffusionParameters;

Console.WriteLine(sd?.Steps);        // 30
Console.WriteLine(sd?.Sampler);      // "Euler a"
Console.WriteLine(sd?.ScheduleType); // "Automatic"
Console.WriteLine(sd?.CfgScale);     // 5.0
Console.WriteLine(sd?.Seed);         // 1704679121
Console.WriteLine(sd?.Model);        // "songmix_v30"
Console.WriteLine(sd?.ModelHash);    // "49da1f19c3"
Console.WriteLine(sd?.ClipSkip);     // 2
Console.WriteLine(sd?.Emphasis);     // "No norm"
Console.WriteLine(sd?.Version);      // "f1.0.0v2-..."
```

### NovelAI typed comment

```csharp
var metadata = await AiMetadataReader.ReadAsync("novelai.png");

Console.WriteLine(metadata.NovelAiComment?.Sampler);  // "k_euler_ancestral"
Console.WriteLine(metadata.NovelAiComment?.Steps);    // 28
Console.WriteLine(metadata.NovelAiComment?.CfgScale); // 7.0
Console.WriteLine(metadata.NovelAiComment?.Seed);     // 1234567890
Console.WriteLine(metadata.NovelAiComment?.Model);    // "nai-diffusion-3"
Console.WriteLine(metadata.NovelAiComment?.Width);    // 832
Console.WriteLine(metadata.NovelAiComment?.Height);   // 1216
```

### ChatGPT / DALL-E (C2PA)

```csharp
var metadata = await AiMetadataReader.ReadAsync("chatgpt.png");

Console.WriteLine(metadata.C2pa?.GeneratorName);  // "gpt-image"
Console.WriteLine(metadata.C2pa?.GeneratedAt);    // 2026-06-03 00:00:00 +00:00
Console.WriteLine(metadata.C2pa?.IsAiGenerated);  // True
Console.WriteLine(metadata.C2pa?.ClaimGenerator); // "OpenAI Media Service API"
```

### Raw entries

```csharp
foreach (var (key, value) in metadata.RawEntries)
{
    Console.WriteLine($"{key}: {value}");
}
```

## AiImageMetadata

| Property | Type | Description |
|---|---|---|
| `Software` | `AiSoftware` | Detected tool (`StableDiffusionWebUi`, `NovelAi`, `ComfyUi`, `C2paCompatible`, `Unknown`) |
| `Prompt` | `string?` | Positive prompt |
| `NegativePrompt` | `string?` | Negative prompt |
| `Parameters` | `string?` | Raw parameter string or JSON |
| `StableDiffusionParameters` | `StableDiffusionParameters?` | Typed SD parameters (Stable Diffusion only) |
| `NovelAiComment` | `NovelAiComment?` | Typed NovelAI comment (NovelAI only) |
| `C2pa` | `C2paMetadata?` | C2PA provenance data (ChatGPT etc.) |
| `RawEntries` | `IReadOnlyDictionary<string, string>` | All text chunk entries |
| `IsSuccess` | `bool` | Whether reading succeeded |
| `Error` | `string?` | Error message if reading failed |

## Requirements

- .NET 10

## License

MIT
