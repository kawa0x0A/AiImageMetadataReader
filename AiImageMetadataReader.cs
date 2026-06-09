using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AiImageMetadataReader;

public static class AiImageMetadataReader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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

    public static async Task<AiImageMetadata> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadCoreAsync(stream, cancellationToken);
        }
        catch (Exception ex)
        {
            return new AiImageMetadata { Error = ex.Message };
        }
    }

    private static async Task<AiImageMetadata> ReadCoreAsync(Stream stream, CancellationToken cancellationToken)
    {
        await ValidateSignatureAsync(stream, cancellationToken);

        var entries = new Dictionary<string, string>();
        C2paMetadata? c2pa = null;
        int? imageWidth = null;
        int? imageHeight = null;

        await foreach (var chunk in ReadChunksAsync(stream, cancellationToken))
        {
            if (chunk.Type == "IHDR")
            {
                imageWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(0, 4));
                imageHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.AsSpan(4, 4));
            }
            else if (chunk.Type is "tEXt" or "zTXt" or "iTXt")
            {
                var resultTask = chunk.Type switch
                {
                    "tEXt" => Task.FromResult(ParseTEXt(chunk.Data)),
                    "zTXt" => ParseZTXtAsync(chunk.Data),
                    "iTXt" => ParseITXtAsync(chunk.Data),
                    _ => Task.FromResult<(string, string)?>(null)
                };
                var result = await resultTask;
                if (result is not null)
                    entries[result.Value.Item1] = result.Value.Item2;
            }
            else if (chunk.Type == "caBX")
            {
                c2pa = C2paParser.Parse(chunk.Data);
            }
        }

        return MetadataBuilder.Build(entries, c2pa, imageWidth, imageHeight);
    }

    private static async Task ValidateSignatureAsync(Stream stream, CancellationToken cancellationToken)
    {
        var sig = new byte[8];
        await stream.ReadExactlyAsync(sig, cancellationToken);
        if (!sig.SequenceEqual(PngSignature))
            throw new InvalidDataException("PNG ファイルではありません。");
    }

    private static readonly HashSet<string> _readChunkTypes = ["IHDR", "tEXt", "zTXt", "iTXt", "caBX"];

    private static async IAsyncEnumerable<(string Type, byte[] Data)> ReadChunksAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lengthBuf = new byte[4];
        var typeBuf = new byte[4];

        while (true)
        {
            if (!await TryReadExactlyAsync(stream, lengthBuf, cancellationToken))
                yield break;

            var dataLength = (int)BinaryPrimitives.ReadUInt32BigEndian(lengthBuf);

            if (dataLength < 0)
                throw new InvalidDataException("チャンクサイズが不正です。");

            if (!await TryReadExactlyAsync(stream, typeBuf, cancellationToken))
                yield break;

            var chunkType = Encoding.ASCII.GetString(typeBuf);

            if (chunkType == "IEND")
                yield break;

            if (_readChunkTypes.Contains(chunkType))
            {
                var data = new byte[dataLength];
                await stream.ReadExactlyAsync(data, cancellationToken);
                await SkipCrcAsync(stream, cancellationToken);
                yield return (chunkType, data);
            }
            else
            {
                await SkipAsync(stream, dataLength + 4, cancellationToken);
            }
        }
    }

    private static (string Key, string Value)? ParseTEXt(byte[] data)
    {
        var nullIndex = Array.IndexOf(data, (byte)0);
        if (nullIndex < 0) return null;

        var key = Encoding.Latin1.GetString(data, 0, nullIndex);
        var value = Encoding.Latin1.GetString(data, nullIndex + 1, data.Length - nullIndex - 1);
        return (key, value);
    }

    private static async Task<(string Key, string Value)?> ParseZTXtAsync(byte[] data)
    {
        var nullIndex = Array.IndexOf(data, (byte)0);
        if (nullIndex < 0 || nullIndex + 2 > data.Length) return null;

        var key = Encoding.Latin1.GetString(data, 0, nullIndex);
        var compressed = data.AsSpan(nullIndex + 2);

        try
        {
            using var ms = new MemoryStream(compressed.ToArray());
            using var deflate = new ZLibStream(ms, CompressionMode.Decompress);
            using var output = new MemoryStream();
            await deflate.CopyToAsync(output);
            return (key, Encoding.Latin1.GetString(output.ToArray()));
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(string Key, string Value)?> ParseITXtAsync(byte[] data)
    {
        var nullIndex = Array.IndexOf(data, (byte)0);
        if (nullIndex < 0 || nullIndex + 3 > data.Length) return null;

        var key = Encoding.UTF8.GetString(data, 0, nullIndex);
        var compressionFlag = data[nullIndex + 1];
        var offset = nullIndex + 3;

        var langEnd = Array.IndexOf(data, (byte)0, offset);
        if (langEnd < 0) return null;
        offset = langEnd + 1;

        var transEnd = Array.IndexOf(data, (byte)0, offset);
        if (transEnd < 0) return null;
        offset = transEnd + 1;

        var textBytes = data.AsSpan(offset).ToArray();

        if (compressionFlag == 1)
        {
            try
            {
                using var ms = new MemoryStream(textBytes);
                using var deflate = new ZLibStream(ms, CompressionMode.Decompress);
                using var output = new MemoryStream();
                await deflate.CopyToAsync(output);
                return (key, Encoding.UTF8.GetString(output.ToArray()));
            }
            catch
            {
                return null;
            }
        }

        return (key, Encoding.UTF8.GetString(textBytes));
    }

    private static async Task SkipCrcAsync(Stream stream, CancellationToken cancellationToken)
        => await SkipAsync(stream, 4, cancellationToken);

    private static async Task SkipAsync(Stream stream, int bytes, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Seek(bytes, SeekOrigin.Current);
            return;
        }

        var buf = new byte[Math.Min(bytes, 4096)];
        var remaining = bytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, Math.Min(remaining, buf.Length)), cancellationToken);
            if (read == 0) return;
            remaining -= read;
        }
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }
}
