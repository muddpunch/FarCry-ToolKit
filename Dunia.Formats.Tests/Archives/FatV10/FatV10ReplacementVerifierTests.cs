using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ReplacementVerifierTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10ReplacementVerifierTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task VerifyRoundTripsPayloadAndPreservesOriginalDataPrefix()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([1, 2, 3]);

        FatV10ReplacementVerificationResult result = await FatV10ReplacementVerifier.VerifyAsync(
            pair,
            0,
            directory,
            token);

        Assert.True(result.IsValid);
        Assert.Equal(result.SourcePayloadSha256, result.RebuiltPayloadSha256);
        Assert.True(result.UnchangedEntriesExact);
        Assert.True(result.OriginalDataPrefixExact);
        Assert.Equal(16, result.RebuiltOffset);
        Assert.Empty(Directory.EnumerateDirectories(directory));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(byte[] data)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), 1);
        FatV10EntryWriter.Write(
            new(1, data.Length, 0, data.Length, FatV10CompressionScheme.None, false),
            fat.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, data);
        return new(fatPath, datPath);
    }
}
