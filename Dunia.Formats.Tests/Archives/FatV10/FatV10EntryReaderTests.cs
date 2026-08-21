using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10EntryReaderTests
{
    [Fact]
    public void ReadDecodesObservedCommonFatEntry()
    {
        byte[] data = Convert.FromHexString("1DDB0A00284E8329E2260000535A10015B0100A0");

        FatV10Entry entry = FatV10EntryReader.Read(data);

        Assert.Equal(0x000ADB1D29834E28UL, entry.NameHash);
        Assert.Equal(2488, entry.UncompressedSize);
        Assert.Equal(142_791_325, entry.Offset);
        Assert.Equal(347, entry.StoredSize);
        Assert.Equal(FatV10CompressionScheme.Lz4, entry.CompressionScheme);
        Assert.False(entry.IsEncrypted);
    }

    [Fact]
    public void ReadRejectsIncorrectRecordSize()
    {
        Assert.Throws<ArgumentException>(() => FatV10EntryReader.Read(new byte[19]));
    }
}

