using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10IndexWriterTests
{
    [Fact]
    public void WriteProducesByteExactRoundTrip()
    {
        byte[] source = CreateIndex();
        using var input = new MemoryStream(source, false);
        FatV10Index index = FatV10IndexReader.Read(input);
        using var output = new MemoryStream();

        FatV10IndexWriter.Write(output, index.Entries);

        Assert.Equal(source, output.ToArray());
    }

    [Fact]
    public void WriteValidatesEveryEntryBeforeWriting()
    {
        FatV10Entry[] entries =
        [
            new(1, 1, 0, 1, FatV10CompressionScheme.None, false),
            new(2, 2, 1, 1, FatV10CompressionScheme.None, false),
        ];
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => FatV10IndexWriter.Write(output, entries));
        Assert.Equal(0, output.Length);
    }

    private static byte[] CreateIndex()
    {
        byte[] bytes = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            (2 * FatV10IndexSummaryReader.EntrySize) +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 2);

        var first = new FatV10Entry(1, 4, 0, 4, FatV10CompressionScheme.None, false);
        var second = new FatV10Entry(2, 8, 5, 3, FatV10CompressionScheme.Lz4, false);
        FatV10EntryWriter.Write(first, bytes.AsSpan(24, 20));
        FatV10EntryWriter.Write(second, bytes.AsSpan(44, 20));
        return bytes;
    }
}
