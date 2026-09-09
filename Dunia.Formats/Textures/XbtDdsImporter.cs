using System.Buffers.Binary;
using System.Globalization;

namespace Dunia.Formats.Textures;

public static class XbtDdsImporter
{
    private const int PrefixLength = 12;
    private const int MaximumHeaderLength = 1024 * 1024;
    private const uint XbtMagic = 0x00584254;
    private const uint DdsMagic = 0x20534444;
    private const uint Dx10FourCc = 0x30315844;

    public static async Task<XbtDdsImportResult> ImportAsync(
        Stream templateXbt,
        Stream replacementDds,
        Stream outputXbt,
        CancellationToken cancellationToken = default)
    {
        ValidateStreams(templateXbt, replacementDds, outputXbt);

        long xbtStart = templateXbt.Position;
        long ddsStart = replacementDds.Position;
        byte[] prefix = new byte[PrefixLength];
        await templateXbt.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix) != XbtMagic)
        {
            throw new InvalidDataException("Template does not contain an XBT header.");
        }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(8));
        long xbtLength = checked(templateXbt.Length - xbtStart);
        if (headerLength < PrefixLength ||
            headerLength > MaximumHeaderLength ||
            headerLength > xbtLength - DdsLayout.MinimumLength)
        {
            throw new InvalidDataException("Template contains an invalid XBT header length.");
        }

        templateXbt.Position = checked(xbtStart + headerLength);
        DdsLayout templateLayout = await ReadLayoutAsync(templateXbt, cancellationToken).ConfigureAwait(false);
        DdsLayout replacementLayout = await ReadLayoutAsync(replacementDds, cancellationToken).ConfigureAwait(false);

        if (templateLayout != replacementLayout)
        {
            throw new InvalidDataException(
                $"Replacement DDS layout does not match the template ({replacementLayout} != {templateLayout}).");
        }

        long templateDdsLength = checked(templateXbt.Length - xbtStart - headerLength);
        long replacementDdsLength = checked(replacementDds.Length - ddsStart);
        if (replacementDdsLength != templateDdsLength)
        {
            throw new InvalidDataException(
                $"Replacement DDS length {replacementDdsLength} does not match template length {templateDdsLength}.");
        }

        templateXbt.Position = xbtStart;
        replacementDds.Position = ddsStart;
        byte[] header = GC.AllocateUninitializedArray<byte>(headerLength);
        await templateXbt.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        await outputXbt.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await replacementDds.CopyToAsync(outputXbt, cancellationToken).ConfigureAwait(false);

        return new(headerLength, replacementDdsLength);
    }

    private static void ValidateStreams(Stream templateXbt, Stream replacementDds, Stream outputXbt)
    {
        ArgumentNullException.ThrowIfNull(templateXbt);
        ArgumentNullException.ThrowIfNull(replacementDds);
        ArgumentNullException.ThrowIfNull(outputXbt);

        if (!templateXbt.CanRead || !templateXbt.CanSeek)
        {
            throw new ArgumentException("Template XBT stream must be readable and seekable.", nameof(templateXbt));
        }

        if (!replacementDds.CanRead || !replacementDds.CanSeek)
        {
            throw new ArgumentException("Replacement DDS stream must be readable and seekable.", nameof(replacementDds));
        }

        if (!outputXbt.CanWrite)
        {
            throw new ArgumentException("Output XBT stream must be writable.", nameof(outputXbt));
        }

        if (ReferenceEquals(templateXbt, replacementDds) ||
            ReferenceEquals(templateXbt, outputXbt) ||
            ReferenceEquals(replacementDds, outputXbt))
        {
            throw new ArgumentException("Template, replacement, and output streams must be different.");
        }
    }

    internal static async Task<DdsLayout> ReadLayoutAsync(Stream input, CancellationToken cancellationToken)
    {
        byte[] header = new byte[DdsLayout.MaximumLength];
        await input.ReadExactlyAsync(header.AsMemory(0, DdsLayout.MinimumLength), cancellationToken).ConfigureAwait(false);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DdsMagic ||
            ReadUInt32(header, 4) != 124 ||
            ReadUInt32(header, 76) != 32)
        {
            throw new InvalidDataException("Input does not contain a valid DDS header.");
        }

        uint height = ReadUInt32(header, 12);
        uint width = ReadUInt32(header, 16);
        uint depth = Math.Max(1, ReadUInt32(header, 24));
        uint mipMapCount = Math.Max(1, ReadUInt32(header, 28));
        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("DDS dimensions must be non-zero.");
        }

        uint fourCc = ReadUInt32(header, 84);
        DdsDx10Layout? dx10 = null;
        if (fourCc == Dx10FourCc)
        {
            await input.ReadExactlyAsync(
                header.AsMemory(DdsLayout.MinimumLength, DdsLayout.MaximumLength - DdsLayout.MinimumLength),
                cancellationToken).ConfigureAwait(false);
            uint arraySize = ReadUInt32(header, 140);
            if (arraySize == 0)
            {
                throw new InvalidDataException("DDS DX10 array size must be non-zero.");
            }

            dx10 = new(
                ReadUInt32(header, 128),
                ReadUInt32(header, 132),
                ReadUInt32(header, 136),
                arraySize,
                ReadUInt32(header, 144));
        }

        return new(
            width,
            height,
            depth,
            mipMapCount,
            ReadUInt32(header, 80),
            fourCc,
            ReadUInt32(header, 88),
            ReadUInt32(header, 92),
            ReadUInt32(header, 96),
            ReadUInt32(header, 100),
            ReadUInt32(header, 104),
            ReadUInt32(header, 112),
            dx10);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));

    internal sealed record DdsLayout(
        uint Width,
        uint Height,
        uint Depth,
        uint MipMapCount,
        uint PixelFormatFlags,
        uint FourCc,
        uint BitsPerPixel,
        uint RedMask,
        uint GreenMask,
        uint BlueMask,
        uint AlphaMask,
        uint Caps2,
        DdsDx10Layout? Dx10)
    {
        public const int MinimumLength = 128;
        public const int MaximumLength = 148;

        public override string ToString() =>
            FormattableString.Invariant(
                $"{Width}x{Height}x{Depth}, mips={MipMapCount}, format={FormatName}");

        private string FormatName => Dx10 is null
            ? FourCc.ToString("X8", CultureInfo.InvariantCulture)
            : Dx10.DxgiFormat.ToString(CultureInfo.InvariantCulture);
    }

    internal sealed record DdsDx10Layout(
        uint DxgiFormat,
        uint ResourceDimension,
        uint MiscFlag,
        uint ArraySize,
        uint MiscFlags2);
}
