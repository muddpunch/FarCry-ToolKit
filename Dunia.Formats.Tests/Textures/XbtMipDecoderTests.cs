using System.Buffers.Binary;
using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtMipDecoderTests
{
    [Fact]
    public async Task DecodeReturnsCompleteMipChainAndPngWriterEncodesSelectedLevel()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        IReadOnlyList<XbtMipImage> mips = await XbtMipDecoder.DecodeAsync(
            new MemoryStream(CreateXbt()), token);

        Assert.Collection(
            mips,
            mip => Assert.Equal((0, 4, 4, 64), (mip.Level, mip.Width, mip.Height, mip.RgbaPixels.Length)),
            mip => Assert.Equal((1, 2, 2, 16), (mip.Level, mip.Width, mip.Height, mip.RgbaPixels.Length)),
            mip => Assert.Equal((2, 1, 1, 4), (mip.Level, mip.Width, mip.Height, mip.RgbaPixels.Length)));

        using var png = new MemoryStream();
        XbtPngExportResult result = await XbtMipPngWriter.WriteAsync(mips[1], png, token);
        Assert.Equal((2, 2), (result.Width, result.Height));
        Assert.Equal<byte>([137, 80, 78, 71, 13, 10, 26, 10], png.ToArray()[..8]);
    }

    private static byte[] CreateXbt()
    {
        byte[] xbt = new byte[36 + 172];
        BinaryPrimitives.WriteUInt32LittleEndian(xbt, 0x00584254);
        BinaryPrimitives.WriteUInt32LittleEndian(xbt.AsSpan(4), 116);
        BinaryPrimitives.WriteUInt32LittleEndian(xbt.AsSpan(8), 36);
        Span<byte> dds = xbt.AsSpan(36);
        BinaryPrimitives.WriteUInt32LittleEndian(dds, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[4..], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[8..], 0x000A1007);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[12..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[16..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[20..], 8);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[28..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[76..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[80..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[84..], 0x30315844);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[108..], 0x00401008);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[128..], 71);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[132..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[140..], 1);
        return xbt;
    }
}
