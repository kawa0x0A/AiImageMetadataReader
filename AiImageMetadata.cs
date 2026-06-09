namespace AiImageMetadataReader;

/// <summary>
/// PNG画像から読み取ったAI生成メタデータ。
/// Stable Diffusion (AUTOMATIC1111/ComfyUI)、NovelAI、および C2PA 対応ツール（ChatGPT など）に対応。
/// </summary>
public sealed class AiImageMetadata
{
    /// <summary>画像の幅（ピクセル）。</summary>
    public int? ImageWidth { get; init; }

    /// <summary>画像の高さ（ピクセル）。</summary>
    public int? ImageHeight { get; init; }

    /// <summary>
    /// Stable Diffusion のパラメータを型付きで返す。Stable Diffusion 以外の場合は null。
    /// </summary>
    public StableDiffusionParameters? StableDiffusionParameters { get; init; }

    /// <summary>画像生成に使用したソフトウェアの種別。</summary>
    public AiSoftware Software { get; init; } = AiSoftware.Unknown;

    /// <summary>プロンプト（正のプロンプト）。</summary>
    public string? Prompt { get; init; }

    /// <summary>ネガティブプロンプト。</summary>
    public string? NegativePrompt { get; init; }

    /// <summary>
    /// その他のパラメータ（Steps, CFG scale, Seed など）。
    /// AUTOMATIC1111 の場合は生の文字列、NovelAI の場合は JSON 文字列。
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>
    /// NovelAI の Comment JSON を型付きで返す。NovelAI 以外の場合は null。
    /// </summary>
    public NovelAiComment? NovelAiComment { get; init; }

    /// <summary>
    /// C2PA メタデータ。ChatGPT (DALL-E) など C2PA に対応したツールで生成された場合に設定される。
    /// </summary>
    public C2paMetadata? C2pa { get; init; }

    /// <summary>tEXt / iTXt / zTXt チャンクの全テキストエントリ。</summary>
    public IReadOnlyDictionary<string, string> RawEntries { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 読み取りに失敗した場合のエラーメッセージ。成功時は null。
    /// </summary>
    public string? Error { get; init; }

    /// <summary>読み取りが成功したかどうか。</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>
/// C2PA (Coalition for Content Provenance and Authenticity) から読み取れる情報。
/// プロンプトは含まれず、生成ツール・日時・AI生成フラグなどが含まれる。
/// </summary>
public sealed class C2paMetadata
{
    /// <summary>生成ツール名（例: "gpt-image"）。</summary>
    public string? GeneratorName { get; init; }

    /// <summary>生成ツールのバージョン（例: "2.0"）。</summary>
    public string? GeneratorVersion { get; init; }

    /// <summary>クレームジェネレータ（例: "OpenAI Media Service API"）。</summary>
    public string? ClaimGenerator { get; init; }

    /// <summary>生成日時（UTC）。</summary>
    public DateTimeOffset? GeneratedAt { get; init; }

    /// <summary>AI生成コンテンツかどうか（trainedAlgorithmicMedia の場合 true）。</summary>
    public bool IsAiGenerated { get; init; }

    /// <summary>IPTC デジタルソース種別 URI。</summary>
    public string? DigitalSourceType { get; init; }
}

/// <summary>Stable Diffusion (AUTOMATIC1111 / WebUI) のパラメータを型付きで表現したクラス。</summary>
public sealed class StableDiffusionParameters
{
    /// <summary>生成ステップ数。</summary>
    public int? Steps { get; init; }

    /// <summary>サンプラー名（例: "Euler a"）。</summary>
    public string? Sampler { get; init; }

    /// <summary>スケジュールタイプ（例: "Automatic"）。</summary>
    public string? ScheduleType { get; init; }

    /// <summary>CFG スケール。</summary>
    public double? CfgScale { get; init; }

    /// <summary>シード値。</summary>
    public long? Seed { get; init; }

    /// <summary>使用したモデル名。</summary>
    public string? Model { get; init; }

    /// <summary>モデルハッシュ。</summary>
    public string? ModelHash { get; init; }

    /// <summary>Clip skip。</summary>
    public int? ClipSkip { get; init; }

    /// <summary>Emphasis（例: "No norm"）。</summary>
    public string? Emphasis { get; init; }

    /// <summary>WebUI のバージョン文字列。</summary>
    public string? Version { get; init; }
}

/// <summary>NovelAI の Comment フィールドを型付きで表現したクラス。</summary>
public sealed class NovelAiComment
{
    /// <summary>使用したサンプラー（例: "k_euler_ancestral"）。</summary>
    public string? Sampler { get; init; }

    /// <summary>生成ステップ数。</summary>
    public int? Steps { get; init; }

    /// <summary>CFG スケール。</summary>
    public double? CfgScale { get; init; }

    /// <summary>シード値。</summary>
    public long? Seed { get; init; }

    /// <summary>使用したモデル名。</summary>
    public string? Model { get; init; }

    /// <summary>画像の幅（ピクセル）。</summary>
    public int? Width { get; init; }

    /// <summary>画像の高さ（ピクセル）。</summary>
    public int? Height { get; init; }

    /// <summary>ネガティブプロンプト（<see cref="AiImageMetadata.NegativePrompt"/> と同じ値）。</summary>
    public string? NegativePrompt { get; init; }
}

public enum AiSoftware
{
    Unknown,
    /// <summary>AUTOMATIC1111 / FORGE など WebUI 系</summary>
    StableDiffusionWebUi,
    /// <summary>NovelAI</summary>
    NovelAi,
    /// <summary>ComfyUI (workflow JSON が埋め込まれている)</summary>
    ComfyUi,
    /// <summary>C2PA 対応ツール（ChatGPT / DALL-E など）</summary>
    C2paCompatible,
}
