using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10EntryWriterTests
{
    [Fact]
    public void WriteReproducesObservedCommonFatEntry()
    {
        var entry = new FatV10Entry(
            0x000ADB1D29834E28UL,
            2488,
            142_791_325,
            347,
            FatV10CompressionScheme.Lz4,
            false);
        Span<byte> output = stackalloc byte[FatV10IndexSummaryReader.EntrySize];

        FatV10EntryWriter.Write(entry, output);

        Assert.Equal("1DDB0A00284E8329E2260000535A10015B0100A0", Convert.ToHexString(output));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0x40000000, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0x20000000, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 0x800000000)]
    public void WriteRejectsValuesOutsidePackedFieldRanges(int size, int storedSize, long offset)
    {
        var entry = new FatV10Entry(
            0,
            size,
            offset,
            storedSize,
            FatV10CompressionScheme.Lz4,
            false);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FatV10EntryWriter.Write(entry, new byte[FatV10IndexSummaryReader.EntrySize]));
    }

    [Fact]
    public void WriteRejectsMismatchedUncompressedEntrySizes()
    {
        var entry = new FatV10Entry(0, 2, 0, 1, FatV10CompressionScheme.None, false);

        Assert.Throws<ArgumentException>(
            () => FatV10EntryWriter.Write(entry, new byte[FatV10IndexSummaryReader.EntrySize]));
    }
}
