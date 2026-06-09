using System.Buffers.Binary;
using System.Text;

namespace AiImageMetadataReader;

public static class JpegAiMetadataReader
{
    private static readonly byte[] JpegSoi = [0xFF, 0xD8];
    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

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
        int? imageWidth = null;
        int? imageHeight = null;

        await foreach (var segment in ReadAppSegmentsAsync(stream, cancellationToken))
        {
            if (segment.Marker == 0xE1) // APP1
            {
                TryParseExif(segment.Data, entries);
            }
            else if (segment.Marker is 0xC0 or 0xC2) // SOF0 / SOF2
            {
                // 1byte: precision, 2bytes: height, 2bytes: width
                if (segment.Data.Length >= 5)
                {
                    imageHeight = (segment.Data[1] << 8) | segment.Data[2];
                    imageWidth = (segment.Data[3] << 8) | segment.Data[4];
                }
            }
        }

        return MetadataBuilder.Build(entries, imageWidth: imageWidth, imageHeight: imageHeight);
    }

    private static async Task ValidateSignatureAsync(Stream stream, CancellationToken cancellationToken)
    {
        var soi = new byte[2];
        await stream.ReadExactlyAsync(soi, cancellationToken);
        if (soi[0] != 0xFF || soi[1] != 0xD8)
            throw new InvalidDataException("JPEG ファイルではありません。");
    }

    private static async IAsyncEnumerable<(byte Marker, byte[] Data)> ReadAppSegmentsAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buf = new byte[4];

        while (true)
        {
            // マーカーの先頭 0xFF を探す
            if (!await TryReadExactlyAsync(stream, buf.AsMemory(0, 2), cancellationToken))
                yield break;

            if (buf[0] != 0xFF)
                yield break;

            var marker = buf[1];

            // SOI / EOI / RST はデータなし
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                continue;

            // セグメント長（マーカーの直後、長さ自身の2バイトを含む）
            if (!await TryReadExactlyAsync(stream, buf.AsMemory(0, 2), cancellationToken))
                yield break;

            var length = (buf[0] << 8) | buf[1];
            if (length < 2)
                yield break;

            var dataLength = length - 2;
            var data = new byte[dataLength];
            await stream.ReadExactlyAsync(data, cancellationToken);

            // APP0–APP15 および SOF0/SOF2 を yield
            if (marker >= 0xE0 && marker <= 0xEF || marker is 0xC0 or 0xC2)
                yield return (marker, data);

            // SOS（0xDA）以降はメタデータがないので終了
            if (marker == 0xDA)
                yield break;
        }
    }

    private static void TryParseExif(byte[] data, Dictionary<string, string> entries)
    {
        // APP1 が EXIF かどうか確認
        if (data.Length < ExifHeader.Length || !data.AsSpan(0, ExifHeader.Length).SequenceEqual(ExifHeader))
            return;

        // TIFF データは Exif ヘッダー（6バイト）の直後
        var tiff = data.AsSpan(ExifHeader.Length);
        if (tiff.Length < 8) return;

        // バイトオーダー判定: "II" = リトルエンディアン, "MM" = ビッグエンディアン
        bool isLittleEndian = tiff[0] == 'I' && tiff[1] == 'I';

        uint ReadU16(ReadOnlySpan<byte> s) => isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(s)
            : BinaryPrimitives.ReadUInt16BigEndian(s);

        uint ReadU32(ReadOnlySpan<byte> s) => isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(s)
            : BinaryPrimitives.ReadUInt32BigEndian(s);

        // IFD0 オフセット
        var ifd0Offset = (int)ReadU32(tiff[4..]);
        if (ifd0Offset + 2 > tiff.Length) return;

        var entryCount = (int)ReadU16(tiff[ifd0Offset..]);
        var ifdStart = ifd0Offset + 2;

        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = ifdStart + i * 12;
            if (entryOffset + 12 > tiff.Length) break;

            var tag = (int)ReadU16(tiff[entryOffset..]);
            var type = (int)ReadU16(tiff[(entryOffset + 2)..]);
            var count = (int)ReadU32(tiff[(entryOffset + 4)..]);
            var valueOrOffset = tiff.Slice(entryOffset + 8, 4);

            switch (tag)
            {
                case 0x9286: // UserComment
                    var uc = ReadUserComment(tiff, type, count, valueOrOffset, ReadU32);
                    if (uc is not null)
                        entries["parameters"] = uc;
                    break;

                case 0x010E: // ImageDescription
                    var desc = ReadAsciiOrOffset(tiff, count, valueOrOffset, ReadU32);
                    if (desc is not null)
                        entries.TryAdd("ImageDescription", desc);
                    break;
            }
        }
    }

    private static string? ReadUserComment(
        ReadOnlySpan<byte> tiff,
        int type,
        int count,
        ReadOnlySpan<byte> valueOrOffset,
        Func<ReadOnlySpan<byte>, uint> readU32)
    {
        // UserComment は UNDEFINED 型(7)、値はオフセット経由
        if (type != 7 || count < 8) return null;

        ReadOnlySpan<byte> raw;
        if (count <= 4)
        {
            raw = valueOrOffset[..count];
        }
        else
        {
            var offset = (int)readU32(valueOrOffset);
            if (offset + count > tiff.Length) return null;
            raw = tiff.Slice(offset, count);
        }

        // 最初の8バイトが文字セット識別子
        var charset = raw[..8];
        var text = raw[8..];

        if (charset.SequenceEqual("ASCII\0\0\0"u8))
            return Encoding.ASCII.GetString(text).Trim('\0').Trim();

        if (charset.SequenceEqual("UNICODE\0"u8))
            return Encoding.Unicode.GetString(text).Trim('\0').Trim();

        if (charset.SequenceEqual("\0\0\0\0\0\0\0\0"u8))
            return Encoding.UTF8.GetString(text).Trim('\0').Trim();

        // 不明な文字セットは UTF-8 として試みる
        return Encoding.UTF8.GetString(text).Trim('\0').Trim();
    }

    private static string? ReadAsciiOrOffset(
        ReadOnlySpan<byte> tiff,
        int count,
        ReadOnlySpan<byte> valueOrOffset,
        Func<ReadOnlySpan<byte>, uint> readU32)
    {
        if (count == 0) return null;

        ReadOnlySpan<byte> raw;
        if (count <= 4)
        {
            raw = valueOrOffset[..count];
        }
        else
        {
            var offset = (int)readU32(valueOrOffset);
            if (offset + count > tiff.Length) return null;
            raw = tiff.Slice(offset, count);
        }

        return Encoding.ASCII.GetString(raw).Trim('\0').Trim();
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0) return false;
            totalRead += read;
        }
        return true;
    }
}
