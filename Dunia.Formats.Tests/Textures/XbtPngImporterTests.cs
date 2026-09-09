using System.Buffers.Binary;
using System.IO.Compression;
using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtPngImporterTests
{
    [Fact]
    public async Task ImportAsyncEncodesPngWithTemplateLayout()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] wrapper = CreateWrapper();
        byte[] template = [.. wrapper, .. CreateBc1Dds(4, 4, 3)];
        byte[] png = CreatePng(4, 4);
        using var output = new MemoryStream();

        XbtDdsImportResult result = await XbtPngImporter.ImportAsync(
            new MemoryStream(template),
            new MemoryStream(png),
            output,
            token);

        byte[] imported = output.ToArray();
        Assert.Equal(36, result.HeaderLength);
        Assert.Equal(172, result.DdsLength);
        Assert.Equal(wrapper, imported[..wrapper.Length]);
        Assert.Equal("DDS "u8.ToArray(), imported[36..40]);
        Assert.Equal(4U, BinaryPrimitives.ReadUInt32LittleEndian(imported.AsSpan(48)));
        Assert.Equal(4U, BinaryPrimitives.ReadUInt32LittleEndian(imported.AsSpan(52)));
        Assert.Equal(3U, BinaryPrimitives.ReadUInt32LittleEndian(imported.AsSpan(64)));
        Assert.Equal(71U, BinaryPrimitives.ReadUInt32LittleEndian(imported.AsSpan(164)));

        using var exportedPng = new MemoryStream();
        XbtPngExportResult export = await XbtPngExporter.ExportAsync(
            new MemoryStream(imported),
            exportedPng,
            token);
        Assert.Equal(4, export.Width);
        Assert.Equal(4, export.Height);
        Assert.Equal<byte>([137, 80, 78, 71, 13, 10, 26, 10], exportedPng.ToArray()[..8]);

        exportedPng.Position = 0;
        using var reimported = new MemoryStream();
        await XbtPngImporter.ImportAsync(
            new MemoryStream(template),
            exportedPng,
            reimported,
            token);
        Assert.Equal(template.Length, reimported.Length);
    }

    [Fact]
    public async Task ImportAsyncRejectsPngDimensionMismatchBeforeWriting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] template = [.. CreateWrapper(), .. CreateBc1Dds(4, 4, 3)];
        using var output = new MemoryStream();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtPngImporter.ImportAsync(
                new MemoryStream(template),
                new MemoryStream(CreatePng(8, 4)),
                output,
                token));

        Assert.Contains("dimensions", exception.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task ImportAsyncRejectsInvalidPngCrcBeforeWriting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] template = [.. CreateWrapper(), .. CreateBc1Dds(4, 4, 3)];
        byte[] png = CreatePng(4, 4);
        png[^5] ^= 0x01;
        using var output = new MemoryStream();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtPngImporter.ImportAsync(
                new MemoryStream(template),
                new MemoryStream(png),
                output,
                token));

        Assert.Contains("CRC", exception.Message);
        Assert.Equal(0, output.Length);
    }

    private static byte[] CreateWrapper()
    {
        byte[] wrapper = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper, 0x00584254);
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(4), 116);
        BinaryPrimitives.WriteUInt32LittleEndian(wrapper.AsSpan(8), 36);
        return wrapper;
    }

    private static byte[] CreateBc1Dds(uint width, uint height, uint mipCount)
    {
        byte[] dds = new byte[172];
        BinaryPrimitives.WriteUInt32LittleEndian(dds, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), height);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(28), mipCount);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(84), 0x30315844);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(128), 71);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(132), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(140), 1);
        return dds;
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), checked((uint)height));
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR"u8, header);

        byte[] rows = new byte[checked((width * 4 + 1) * height)];
        for (int y = 0; y < height; y++)
        {
            int offset = y * (width * 4 + 1) + 1;
            for (int x = 0; x < width; x++)
            {
                rows[offset++] = checked((byte)(x * 20));
                rows[offset++] = checked((byte)(y * 20));
                rows[offset++] = 0x80;
                rows[offset++] = 0xFF;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, checked((uint)data.Length));
        output.Write(value);
        output.Write(type);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(value, ComputeCrc(type, data));
        output.Write(value);
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
