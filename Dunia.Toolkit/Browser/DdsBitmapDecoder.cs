using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dunia.Toolkit.Browser;

internal static class DdsBitmapDecoder
{
    private const long MaximumPixels = 64L * 1024 * 1024;

    public static async Task<BitmapSource> DecodeAsync(
        Stream dds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dds);

        try
        {
            BitmapSource bitmap = BitmapDecoder.Create(
                dds,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad).Frames[0];
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException)
        {
            dds.Position = 0;
        }

        Memory2D<ColorRgba32> image = await new BcDecoder()
            .Decode2DAsync(dds, cancellationToken)
            .ConfigureAwait(false);

        return CreateBitmap(image);
    }

    private static BitmapSource CreateBitmap(Memory2D<ColorRgba32> image)
    {
        int width = image.Width;
        int height = image.Height;
        long pixelCount = checked((long)width * height);

        if (width <= 0 || height <= 0 || pixelCount > MaximumPixels)
        {
            throw new InvalidDataException($"Unsupported texture dimensions: {width} x {height}.");
        }

        int stride = checked(width * 4);
        byte[] bgra = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        Span2D<ColorRgba32> source = image.Span;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x++)
            {
                ColorRgba32 color = source[y, x];
                int offset = rowOffset + (x * 4);
                bgra[offset] = color.b;
                bgra[offset + 1] = color.g;
                bgra[offset + 2] = color.r;
                bgra[offset + 3] = color.a;
            }
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bgra,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
