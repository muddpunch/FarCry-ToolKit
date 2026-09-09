using System.Buffers.Binary;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace Dunia.Formats.Textures;

public static class XbtPngImporter
{
    private const int PrefixLength = 12;
    private const int MaximumHeaderLength = 1024 * 1024;
    private const uint XbtMagic = 0x00584254;
    private const uint DdsMagic = 0x20534444;
    private const uint Dx10FourCc = 0x30315844;

    public static async Task<XbtDdsImportResult> ImportAsync(
        Stream templateXbt,
        Stream png,
        Stream outputXbt,
        CancellationToken cancellationToken = default)
    {
        ValidateStreams(templateXbt, png, outputXbt);
        long xbtStart = templateXbt.Position;
        byte[] prefix = new byte[PrefixLength];
        await templateXbt.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix) != XbtMagic)
        {
            throw new InvalidDataException("Template does not contain an XBT header.");
        }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(8));
        if (headerLength < PrefixLength ||
            headerLength > MaximumHeaderLength ||
            headerLength > templateXbt.Length - xbtStart - 128)
        {
            throw new InvalidDataException("Template contains an invalid XBT header length.");
        }

        templateXbt.Position = checked(xbtStart + headerLength);
        XbtDdsImporter.DdsLayout layout = await XbtDdsImporter
            .ReadLayoutAsync(templateXbt, cancellationToken)
            .ConfigureAwait(false);
        CompressionFormat format = ResolveFormat(layout);
        PngRgbaImage image = await PngRgbaDecoder.DecodeAsync(png, cancellationToken).ConfigureAwait(false);

        if ((uint)image.Width != layout.Width || (uint)image.Height != layout.Height)
        {
            throw new InvalidDataException(
                $"PNG dimensions {image.Width}x{image.Height} do not match template dimensions {layout.Width}x{layout.Height}.");
        }

        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = layout.MipMapCount > 1;
        encoder.OutputOptions.MaxMipMapLevel = checked((int)layout.MipMapCount);
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;
        encoder.OutputOptions.DdsPreferDxt10Header = true;

        using var dds = new MemoryStream();
        await encoder.EncodeToStreamAsync(
            image.Pixels,
            image.Width,
            image.Height,
            PixelFormat.Rgba32,
            dds,
            cancellationToken).ConfigureAwait(false);
        PatchDxgiFormat(dds, layout.Dx10!.DxgiFormat);

        dds.Position = 0;
        templateXbt.Position = xbtStart;
        return await XbtDdsImporter
            .ImportAsync(templateXbt, dds, outputXbt, cancellationToken)
            .ConfigureAwait(false);
    }

    private static CompressionFormat ResolveFormat(XbtDdsImporter.DdsLayout layout)
    {
        XbtDdsImporter.DdsDx10Layout? dx10 = layout.Dx10;
        if (layout.Depth != 1 || dx10 is null || dx10.ResourceDimension != 3 || dx10.ArraySize != 1 || dx10.MiscFlag != 0)
        {
            throw new NotSupportedException("PNG import currently supports only non-array 2D DX10 textures.");
        }

        return dx10.DxgiFormat switch
        {
            71 or 72 => CompressionFormat.Bc1,
            74 or 75 => CompressionFormat.Bc2,
            77 or 78 => CompressionFormat.Bc3,
            80 => CompressionFormat.Bc4,
            83 => CompressionFormat.Bc5,
            98 or 99 => CompressionFormat.Bc7,
            _ => throw new NotSupportedException($"PNG import does not support DXGI format {dx10.DxgiFormat}."),
        };
    }

    private static void PatchDxgiFormat(MemoryStream dds, uint dxgiFormat)
    {
        Span<byte> data = dds.GetBuffer().AsSpan(0, checked((int)dds.Length));
        if (data.Length < 148 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data) != DdsMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[84..]) != Dx10FourCc)
        {
            throw new InvalidDataException("DDS encoder did not produce the required DX10 header.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data[128..], dxgiFormat);
    }

    private static void ValidateStreams(Stream templateXbt, Stream png, Stream outputXbt)
    {
        ArgumentNullException.ThrowIfNull(templateXbt);
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(outputXbt);

        if (!templateXbt.CanRead || !templateXbt.CanSeek)
        {
            throw new ArgumentException("Template XBT stream must be readable and seekable.", nameof(templateXbt));
        }

        if (!png.CanRead)
        {
            throw new ArgumentException("PNG stream must be readable.", nameof(png));
        }

        if (!outputXbt.CanWrite)
        {
            throw new ArgumentException("Output XBT stream must be writable.", nameof(outputXbt));
        }

        if (ReferenceEquals(templateXbt, png) ||
            ReferenceEquals(templateXbt, outputXbt) ||
            ReferenceEquals(png, outputXbt))
        {
            throw new ArgumentException("Template, PNG, and output streams must be different.");
        }
    }
}
