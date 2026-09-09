using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dunia.Formats.Textures;

namespace Dunia.Toolkit.Browser;

internal static class TextureBitmapRenderer
{
    public static BitmapSource Render(XbtMipImage image, TextureChannel channel)
    {
        ArgumentNullException.ThrowIfNull(image);
        int stride = checked(image.Width * 4);
        byte[] bgra = GC.AllocateUninitializedArray<byte>(checked(stride * image.Height));
        ReadOnlySpan<byte> rgba = image.RgbaPixels;
        for (int source = 0, target = 0; source < rgba.Length; source += 4, target += 4)
        {
            byte red = rgba[source];
            byte green = rgba[source + 1];
            byte blue = rgba[source + 2];
            byte alpha = rgba[source + 3];
            (byte outputRed, byte outputGreen, byte outputBlue, byte outputAlpha) = channel switch
            {
                TextureChannel.Rgba => (red, green, blue, alpha),
                TextureChannel.Rgb => (red, green, blue, byte.MaxValue),
                TextureChannel.Red => (red, red, red, byte.MaxValue),
                TextureChannel.Green => (green, green, green, byte.MaxValue),
                TextureChannel.Blue => (blue, blue, blue, byte.MaxValue),
                TextureChannel.Alpha => (alpha, alpha, alpha, byte.MaxValue),
                _ => throw new ArgumentOutOfRangeException(nameof(channel)),
            };
            bgra[target] = outputBlue;
            bgra[target + 1] = outputGreen;
            bgra[target + 2] = outputRed;
            bgra[target + 3] = outputAlpha;
        }

        BitmapSource bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
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
