using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbArchiveScannerTests
{
    [Fact]
    public async Task ScanFindsFcbSignatureAndReportsSkippedEntries()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] data = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        FatV10Entry[] entries =
        [
            new(1, 16, 0, 16, FatV10CompressionScheme.None, false),
            new(2, 16, 16, 16, FatV10CompressionScheme.None, false),
            new(3, 4, 0, 4, FatV10CompressionScheme.None, false),
        ];
        var index = new FatV10Index(
            new(1, entries.Length, FatV10IndexSummaryReader.EntrySize, 92),
            Array.AsReadOnly(entries));

        FcbArchiveScanResult result = await FcbArchiveScanner.ScanAsync(
            new MemoryStream(data, false),
            index,
            cancellationToken: token);

        FcbArchiveMatch match = Assert.Single(result.Matches);
        Assert.Equal(0, match.EntryIndex);
        Assert.Equal(2, result.ScannedEntryCount);
        Assert.Equal(1, result.SkippedEntryCount);
    }
}
