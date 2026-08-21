using System.Buffers.Binary;
using Dunia.Formats.Archives.Recon;

namespace Dunia.Formats.Tests.Archives.Recon;

public sealed class FatPrefixProbeTests
{
    [Fact]
    public void ReadReportsBothByteOrdersWithoutAssumingV10Layout()
    {
        byte[] data = new byte[32];
        "FAT2"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 10);

        FatPrefix result = FatPrefixProbe.Read(new MemoryStream(data));

        Assert.Equal("FAT2", result.MagicAscii);
        Assert.Equal(10, result.VersionLittleEndian);
        Assert.Equal(0x0A000000, result.VersionBigEndian);
        Assert.Equal(Convert.ToHexString(data), result.Hex);
    }

    [Fact]
    public void ReadRejectsTruncatedInput()
    {
        Assert.Throws<InvalidDataException>(() =>
            FatPrefixProbe.Read(new MemoryStream(new byte[7])));
    }
}

