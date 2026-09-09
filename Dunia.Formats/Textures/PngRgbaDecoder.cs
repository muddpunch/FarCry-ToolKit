using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;

namespace Dunia.Formats.Textures;

internal static class PngRgbaDecoder
{
    private const int MaximumEncodedLength = 256 * 1024 * 1024;
    private const long MaximumPixels = 64L * 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static async Task<PngRgbaImage> DecodeAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            throw new ArgumentException("PNG stream must be readable.", nameof(input));
        }

        using var encoded = new MemoryStream();
        await CopyWithLimitAsync(input, encoded, cancellationToken).ConfigureAwait(false);
        return Decode(encoded.GetBuffer().AsSpan(0, checked((int)encoded.Length)));
    }

    private static PngRgbaImage Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("Input does not contain a PNG signature.");
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        int channels = 0;
        byte colorType = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();
        bool sawHeader = false;
        bool sawEnd = false;

        while (offset <= png.Length - 12)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]));
            offset += 4;
            if (length < 0 || length > png.Length - offset - 8)
            {
                throw new InvalidDataException("PNG chunk exceeds the input bounds.");
            }

            ReadOnlySpan<byte> type = png.Slice(offset, 4);
            ReadOnlySpan<byte> data = png.Slice(offset + 4, length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 4 + length, 4));
            if (ComputeCrc(type, data) != expectedCrc)
            {
                throw new InvalidDataException("PNG chunk CRC is invalid.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13 || offset != Signature.Length + 4)
                {
                    throw new InvalidDataException("PNG contains an invalid IHDR chunk.");
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                colorType = data[9];
                channels = colorType switch
                {
                    0 => 1,
                    2 => 3,
                    3 => 1,
                    4 => 2,
                    6 => 4,
                    _ => throw new InvalidDataException($"Unsupported PNG color type {colorType}."),
                };
                if (width <= 0 || height <= 0 || checked((long)width * height) > MaximumPixels)
                {
                    throw new InvalidDataException($"Unsupported PNG dimensions: {width} x {height}.");
                }

                if (data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                {
                    throw new InvalidDataException("Only non-interlaced 8-bit PNG images are supported.");
                }

                sawHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (!sawHeader || length == 0 || length % 3 != 0 || length > 768)
                {
                    throw new InvalidDataException("PNG contains an invalid palette.");
                }

                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader)
                {
                    throw new InvalidDataException("PNG IDAT precedes IHDR.");
                }

                idat.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0)
                {
                    throw new InvalidDataException("PNG contains an invalid IEND chunk.");
                }

                sawEnd = true;
                break;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException($"Unsupported critical PNG chunk {System.Text.Encoding.ASCII.GetString(type)}.");
            }

            offset = checked(offset + 8 + length);
        }

        if (!sawHeader || !sawEnd || idat.Length == 0 || (colorType == 3 && palette is null))
        {
            throw new InvalidDataException("PNG is missing required chunks.");
        }

        int stride = checked(width * channels);
        byte[] filtered = GC.AllocateUninitializedArray<byte>(checked((stride + 1) * height));
        idat.Position = 0;
        using (var inflater = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            inflater.ReadExactly(filtered);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("PNG decompressed data exceeds the declared dimensions.");
            }
        }

        byte[] pixels = Unfilter(filtered, width, height, channels);
        return new(width, height, ConvertToRgba(pixels, width, height, colorType, palette, transparency));
    }

    private static byte[] Unfilter(byte[] filtered, int width, int height, int channels)
    {
        int stride = checked(width * channels);
        byte[] result = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        for (int y = 0; y < height; y++)
        {
            int sourceOffset = y * (stride + 1);
            int rowOffset = y * stride;
            byte filter = filtered[sourceOffset++];
            if (filter > 4)
            {
                throw new InvalidDataException($"Unsupported PNG filter {filter}.");
            }

            for (int x = 0; x < stride; x++)
            {
                byte raw = filtered[sourceOffset + x];
                byte left = x >= channels ? result[rowOffset + x - channels] : (byte)0;
                byte above = y > 0 ? result[rowOffset + x - stride] : (byte)0;
                byte upperLeft = y > 0 && x >= channels ? result[rowOffset + x - stride - channels] : (byte)0;
                result[rowOffset + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + above)),
                    3 => unchecked((byte)(raw + ((left + above) >> 1))),
                    4 => unchecked((byte)(raw + Paeth(left, above, upperLeft))),
                    _ => throw new UnreachableException(),
                };
            }
        }

        return result;
    }

    private static byte[] ConvertToRgba(
        byte[] source,
        int width,
        int height,
        byte colorType,
        byte[]? palette,
        byte[]? transparency)
    {
        byte[] rgba = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        int sourceOffset = 0;
        for (int target = 0; target < rgba.Length; target += 4)
        {
            switch (colorType)
            {
                case 0:
                    byte gray = source[sourceOffset++];
                    rgba[target] = gray;
                    rgba[target + 1] = gray;
                    rgba[target + 2] = gray;
                    rgba[target + 3] = IsTransparentGray(gray, transparency) ? (byte)0 : (byte)255;
                    break;
                case 2:
                    byte red = source[sourceOffset++];
                    byte green = source[sourceOffset++];
                    byte blue = source[sourceOffset++];
                    rgba[target] = red;
                    rgba[target + 1] = green;
                    rgba[target + 2] = blue;
                    rgba[target + 3] = IsTransparentRgb(red, green, blue, transparency) ? (byte)0 : (byte)255;
                    break;
                case 3:
                    int index = source[sourceOffset++];
                    int paletteOffset = checked(index * 3);
                    if (paletteOffset > palette!.Length - 3)
                    {
                        throw new InvalidDataException("PNG palette index is out of range.");
                    }

                    rgba[target] = palette[paletteOffset];
                    rgba[target + 1] = palette[paletteOffset + 1];
                    rgba[target + 2] = palette[paletteOffset + 2];
                    rgba[target + 3] = index < transparency?.Length ? transparency[index] : (byte)255;
                    break;
                case 4:
                    gray = source[sourceOffset++];
                    rgba[target] = gray;
                    rgba[target + 1] = gray;
                    rgba[target + 2] = gray;
                    rgba[target + 3] = source[sourceOffset++];
                    break;
                case 6:
                    rgba[target] = source[sourceOffset++];
                    rgba[target + 1] = source[sourceOffset++];
                    rgba[target + 2] = source[sourceOffset++];
                    rgba[target + 3] = source[sourceOffset++];
                    break;
            }
        }

        return rgba;
    }

    private static bool IsTransparentGray(byte gray, byte[]? transparency) =>
        transparency is { Length: 2 } && BinaryPrimitives.ReadUInt16BigEndian(transparency) == gray;

    private static bool IsTransparentRgb(byte red, byte green, byte blue, byte[]? transparency) =>
        transparency is { Length: 6 } &&
        BinaryPrimitives.ReadUInt16BigEndian(transparency) == red &&
        BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) == green &&
        BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) == blue;

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
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

    private static async Task CopyWithLimitAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[80 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            if (output.Length > MaximumEncodedLength - read)
            {
                throw new InvalidDataException("PNG exceeds the 256 MB encoded-size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed record PngRgbaImage(int Width, int Height, byte[] Pixels);
