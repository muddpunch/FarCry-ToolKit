using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10IndexSummaryReaderTests
{
    [Fact]
    public void ReadParsesValidatedEnvelopeAndRestoresPosition()
    {
        using MemoryStream input = CreateIndex(3);
        input.Position = 7;

        FatV10IndexSummary summary = FatV10IndexSummaryReader.Read(input);

        Assert.Equal(1, summary.Platform);
        Assert.Equal(3, summary.EntryCount);
        Assert.Equal(20, summary.EntrySize);
        Assert.Equal(92, summary.IndexLength);
        Assert.Equal(7, input.Position);
    }

    [Fact]
    public void ReadAcceptsObservedEmptyIndexShape()
    {
        FatV10IndexSummary summary = FatV10IndexSummaryReader.Read(CreateIndex(0));

        Assert.Equal(0, summary.EntryCount);
        Assert.Equal(32, summary.IndexLength);
    }

    [Fact]
    public void ReadRejectsLengthMismatch()
    {
        using MemoryStream input = CreateIndex(1);
        input.SetLength(input.Length - 1);

        Assert.Throws<InvalidDataException>(() => FatV10IndexSummaryReader.Read(input));
    }

    [Fact]
    public void ReadRejectsUnsupportedTrailer()
    {
        using MemoryStream input = CreateIndex(0);
        input.Position = input.Length - 1;
        input.WriteByte(1);

        Assert.Throws<InvalidDataException>(() => FatV10IndexSummaryReader.Read(input));
    }

    private static MemoryStream CreateIndex(int entryCount)
    {
        byte[] bytes = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            (entryCount * FatV10IndexSummaryReader.EntrySize) +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), entryCount);
        return new(bytes, true);
    }
}
