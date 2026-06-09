namespace AiImageMetadataReader;

public static class AiMetadataReader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8];

    private static readonly HashSet<string> SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".jpe", ".jfif"];

    /// <summary>
    /// ファイルパスから画像フォーマットを自動判別してメタデータを読み取る。
    /// </summary>
    public static async Task<AiImageMetadata> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            return await ReadCoreAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            return new AiImageMetadata { Error = ex.Message };
        }
    }

    /// <summary>
    /// シーク可能なストリームからフォーマットを自動判別してメタデータを読み取る。
    /// </summary>
    public static async Task<AiImageMetadata> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!stream.CanSeek)
                throw new NotSupportedException("フォーマット自動判別にはシーク可能なストリームが必要です。拡張子ヒントを渡す ReadAsync(stream, hint) を使用してください。");

            return await ReadCoreAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            return new AiImageMetadata { Error = ex.Message };
        }
    }

    /// <summary>
    /// 非シーク可能なストリームから、拡張子ヒントを使ってメタデータを読み取る。
    /// </summary>
    /// <param name="hint">拡張子（例: ".png", ".jpg"）またはファイル名。</param>
    public static async Task<AiImageMetadata> ReadAsync(Stream stream, string hint, CancellationToken cancellationToken = default)
    {
        try
        {
            var format = DetectFormatFromHint(hint);
            return format switch
            {
                ImageFormat.Png => await AiImageMetadataReader.ReadAsync(stream, cancellationToken),
                ImageFormat.Jpeg => await JpegAiMetadataReader.ReadAsync(stream, cancellationToken),
                _ => new AiImageMetadata { Error = $"対応していない拡張子です: {hint}" },
            };
        }
        catch (Exception ex)
        {
            return new AiImageMetadata { Error = ex.Message };
        }
    }

    /// <summary>
    /// ディレクトリ内の対応画像ファイル（PNG / JPEG）を非同期で順番に読み取る。
    /// </summary>
    /// <param name="directoryPath">検索するディレクトリのパス。</param>
    /// <param name="searchOption">サブディレクトリを含めるかどうか。</param>
    public static async IAsyncEnumerable<(string FilePath, AiImageMetadata Metadata)> ReadDirectoryAsync(
        string directoryPath,
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = Directory.EnumerateFiles(directoryPath, "*.*", searchOption)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await ReadAsync(file, cancellationToken);
            yield return (file, metadata);
        }
    }

    private static async Task<AiImageMetadata> ReadCoreAsync(Stream stream, CancellationToken cancellationToken)
    {
        var format = await DetectFormatAsync(stream, cancellationToken);
        stream.Seek(0, SeekOrigin.Begin);

        return format switch
        {
            ImageFormat.Png => await AiImageMetadataReader.ReadAsync(stream, cancellationToken),
            ImageFormat.Jpeg => await JpegAiMetadataReader.ReadAsync(stream, cancellationToken),
            _ => new AiImageMetadata { Error = "対応していない画像フォーマットです。PNG または JPEG を指定してください。" },
        };
    }

    private static async Task<ImageFormat> DetectFormatAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);

        if (read >= PngSignature.Length && header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            return ImageFormat.Png;

        if (read >= JpegSignature.Length && header.AsSpan(0, JpegSignature.Length).SequenceEqual(JpegSignature))
            return ImageFormat.Jpeg;

        return ImageFormat.Unknown;
    }

    private static ImageFormat DetectFormatFromHint(string hint)
    {
        var ext = Path.GetExtension(hint).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = hint.ToLowerInvariant();

        return ext switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => ImageFormat.Jpeg,
            _ => ImageFormat.Unknown,
        };
    }

    private enum ImageFormat { Unknown, Png, Jpeg }
}
