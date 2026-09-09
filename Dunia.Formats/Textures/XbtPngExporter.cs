using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;

namespace Dunia.Formats.Textures;

public static class XbtPngExporter
{
    private const int MaximumDdsLength = 256 * 1024 * 1024;
    private const long MaximumPixels = 64L * 1024 * 1024;

    public static async Task<XbtPngExportResult> ExportAsync(
        Stream xbt,
        Stream png,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xbt);
        ArgumentNullException.ThrowIfNull(png);
        if (!xbt.CanRead)
        {
            throw new ArgumentException("XBT stream must be readable.", nameof(xbt));
        }

        if (!png.CanWrite)
        {
            throw new ArgumentException("PNG stream must be writable.", nameof(png));
        }

        if (ReferenceEquals(xbt, png))
        {
            throw new ArgumentException("Input and output streams must be different.", nameof(png));
        }

        using var dds = new LengthLimitedMemoryStream(MaximumDdsLength);
        XbtDdsExtractionResult extraction = await XbtDdsExtractor
            .ExtractAsync(xbt, dds, cancellationToken)
            .ConfigureAwait(false);
        if (extraction.DdsLength > MaximumDdsLength)
        {
            throw new InvalidDataException("DDS exceeds the 256 MB decode limit.");
        }

        dds.Position = 0;
        Memory2D<ColorRgba32> decoded = await new BcDecoder()
            .Decode2DAsync(dds, cancellationToken)
            .ConfigureAwait(false);
        int width = decoded.Width;
        int height = decoded.Height;
        if (width <= 0 || height <= 0 || checked((long)width * height) > MaximumPixels)
        {
            throw new InvalidDataException($"Unsupported texture dimensions: {width} x {height}.");
        }

        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
        Span2D<ColorRgba32> source = decoded.Span;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ColorRgba32 color = source[y, x];
                int offset = checked((y * width + x) * 4);
                pixels[offset] = color.r;
                pixels[offset + 1] = color.g;
                pixels[offset + 2] = color.b;
                pixels[offset + 3] = color.a;
            }
        }

        long length = await PngRgbaEncoder
            .EncodeAsync(new(width, height, pixels), png, cancellationToken)
            .ConfigureAwait(false);
        return new(width, height, length);
    }

    private sealed class LengthLimitedMemoryStream(int maximumLength) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
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
