using System.Buffers.Binary;
using Dunia.Formats.Meshes;

namespace Dunia.Formats.Tests.Meshes;

public sealed class XbgSummaryReaderTests
{
    [Fact]
    public void ReadReturnsValidatedFarCry5MeshSummary()
    {
        byte[] data = BuildXbg(("LTMR", 3), ("SDOL", 2));
        using var input = new MemoryStream(data, false);

        XbgSummary result = XbgSummaryReader.Read(input);

        Assert.Equal(XbgSummaryReader.FarCry5Version, result.Version);
        Assert.Equal(3, result.MaterialCount);
        Assert.Equal(2, result.LodCount);
        Assert.Equal(["LTMR", "SDOL"], result.Chunks.Select(chunk => chunk.Name));
    }

    [Fact]
    public void ReadRejectsUnsupportedVersion()
    {
        byte[] data = BuildXbg(("SDOL", 1));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
        using var input = new MemoryStream(data, false);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => XbgSummaryReader.Read(input));

        Assert.Contains("0x00000001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRejectsChunkOutsideFileBounds()
    {
        byte[] data = BuildXbg(("SDOL", 1));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(40), 4096);
        using var input = new MemoryStream(data, false);

        Assert.Throws<InvalidDataException>(() => XbgSummaryReader.Read(input));
    }

    private static byte[] BuildXbg(params (string Name, int Count)[] chunks)
    {
        byte[] data = new byte[32 + (chunks.Length * 24)];
        "HSEM"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), XbgSummaryReader.FarCry5Version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), checked((uint)data.Length - 12));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), checked((uint)chunks.Length));

        int offset = 32;
        foreach ((string name, int count) in chunks)
        {
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(data, offset);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8), 24);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 12), 4);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 20), count);
            offset += 24;
        }

        return data;
    }
}
