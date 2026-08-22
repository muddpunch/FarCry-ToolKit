using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbArchiveAnalyzerTests
{
    [Fact]
    public async Task AnalyzeParsesFcbAndAggregatesHashOccurrences()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] fcb = CreateFcb();
        byte[] data = new byte[fcb.Length + 16];
        fcb.CopyTo(data, 0);
        FatV10Entry[] entries =
        [
            new(0x11, fcb.Length, 0, fcb.Length, FatV10CompressionScheme.None, false),
            new(0x22, 16, fcb.Length, 16, FatV10CompressionScheme.None, false),
        ];
        var index = new FatV10Index(
            new(1, entries.Length, FatV10IndexSummaryReader.EntrySize, 72),
            Array.AsReadOnly(entries));

        FcbArchiveAnalysisResult result = await FcbArchiveAnalyzer.AnalyzeAsync(
            new MemoryStream(data, false),
            index,
            cancellationToken: token);

        FcbArchiveResource resource = Assert.Single(result.Resources);
        Assert.Equal(0, resource.EntryIndex);
        Assert.Equal(0x11223344U, Assert.Single(result.TypeHashOccurrences).Key);
        Assert.Equal(0x55667788U, Assert.Single(result.FieldHashOccurrences).Key);
        Assert.Equal(2, result.ScannedEntryCount);
        Assert.Equal(0, result.SkippedEntryCount);
    }

    private static byte[] CreateFcb()
    {
        byte[] data = new byte[FcbReader.HeaderSize + 13];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        data[FcbReader.HeaderSize] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(FcbReader.HeaderSize + 1), 0x11223344);
        data[FcbReader.HeaderSize + 5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(FcbReader.HeaderSize + 6), 0x55667788);
        data[FcbReader.HeaderSize + 10] = 2;
        data[FcbReader.HeaderSize + 11] = 1;
        data[FcbReader.HeaderSize + 12] = 2;
        return data;
    }
}
