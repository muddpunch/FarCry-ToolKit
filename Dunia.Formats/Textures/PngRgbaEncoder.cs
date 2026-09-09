using System.Buffers.Binary;
using System.IO.Compression;

namespace Dunia.Formats.Textures;

internal static class PngRgbaEncoder
{
    private const long MaximumPixels = 64L * 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static async Task<long> EncodeAsync(
        PngRgbaImage image,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite || !output.CanSeek)
        {
            throw new ArgumentException("PNG output stream must be writable and seekable.", nameof(output));
        }

        long pixelCount = checked((long)image.Width * image.Height);
        if (image.Width <= 0 || image.Height <= 0 || pixelCount > MaximumPixels ||
            image.Pixels.LongLength != checked(pixelCount * 4))
        {
            throw new InvalidDataException("RGBA image dimensions or pixel length are invalid.");
        }

        long initialPosition = output.CanSeek ? output.Position : 0;
        await output.WriteAsync(Signature.ToArray(), cancellationToken).ConfigureAwait(false);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), checked((uint)image.Height));
        header[8] = 8;
        header[9] = 6;
        await WriteChunkAsync(output, "IHDR"u8.ToArray(), header, cancellationToken).ConfigureAwait(false);

        int stride = checked(image.Width * 4);
        using var compressed = new MemoryStream();
        await using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] row = GC.AllocateUninitializedArray<byte>(stride + 1);
            row[0] = 0;
            for (int y = 0; y < image.Height; y++)
            {
                image.Pixels.AsSpan(y * stride, stride).CopyTo(row.AsSpan(1));
                await zlib.WriteAsync(row, cancellationToken).ConfigureAwait(false);
            }
        }

        await WriteChunkAsync(output, "IDAT"u8.ToArray(), compressed.ToArray(), cancellationToken).ConfigureAwait(false);
        await WriteChunkAsync(output, "IEND"u8.ToArray(), [], cancellationToken).ConfigureAwait(false);
        return output.Position - initialPosition;
    }

    private static async Task WriteChunkAsync(
        Stream output,
        byte[] type,
        byte[] data,
        CancellationToken cancellationToken)
    {
        byte[] value = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, checked((uint)data.Length));
        await output.WriteAsync(value, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(type, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteUInt32BigEndian(value, ComputeCrc(type, data));
        await output.WriteAsync(value, cancellationToken).ConfigureAwait(false);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in type)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (byte value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc >> 1) ^ (0xEDB88320U & unchecked((uint)-(int)(crc & 1)));
        }

        return crc;
    }
}
