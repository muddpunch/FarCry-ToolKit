using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;

namespace Dunia.Formats.Textures;

public static class XbtMipDecoder
{
    private const int MaximumDdsLength = 256 * 1024 * 1024;
    private const long MaximumPixels = 64L * 1024 * 1024;

    public static async Task<IReadOnlyList<XbtMipImage>> DecodeAsync(
        Stream xbt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xbt);
        if (!xbt.CanRead)
        {
            throw new ArgumentException("XBT stream must be readable.", nameof(xbt));
        }

        using var dds = new LengthLimitedMemoryStream(MaximumDdsLength);
        await XbtDdsExtractor.ExtractAsync(xbt, dds, cancellationToken).ConfigureAwait(false);
        dds.Position = 0;
        Memory2D<ColorRgba32>[] decoded = await new BcDecoder()
            .DecodeAllMipMaps2DAsync(dds, cancellationToken)
            .ConfigureAwait(false);
        if (decoded.Length == 0)
        {
            throw new InvalidDataException("DDS contains no mip levels.");
        }

        long totalPixels = 0;
        var images = new XbtMipImage[decoded.Length];
        for (int level = 0; level < decoded.Length; level++)
        {
            Memory2D<ColorRgba32> image = decoded[level];
            int width = image.Width;
            int height = image.Height;
            long pixels = checked((long)width * height);
            totalPixels = checked(totalPixels + pixels);
            if (width <= 0 || height <= 0 || totalPixels > MaximumPixels)
            {
                throw new InvalidDataException("Decoded mip chain exceeds the 64-megapixel safety limit.");
            }

            byte[] rgba = GC.AllocateUninitializedArray<byte>(checked((int)pixels * 4));
            Span2D<ColorRgba32> source = image.Span;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ColorRgba32 color = source[y, x];
                    int offset = checked((y * width + x) * 4);
                    rgba[offset] = color.r;
                    rgba[offset + 1] = color.g;
                    rgba[offset + 2] = color.b;
                    rgba[offset + 3] = color.a;
                }
            }

            images[level] = new(level, width, height, rgba);
        }

        return Array.AsReadOnly(images);
    }

    private sealed class LengthLimitedMemoryStream(int maximumLength) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void EnsureCapacity(int count)
        {
            if (Position > maximumLength - count)
            {
                throw new InvalidDataException("DDS exceeds the 256 MB decode limit.");
            }
        }
    }
}
