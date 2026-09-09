namespace Dunia.Formats.Textures;

public static class XbtMipPngWriter
{
    public static async Task<XbtPngExportResult> WriteAsync(
        XbtMipImage image,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        long length = await PngRgbaEncoder.EncodeAsync(
            new(image.Width, image.Height, image.RgbaPixels), output, cancellationToken).ConfigureAwait(false);
        return new(image.Width, image.Height, length);
    }
}
