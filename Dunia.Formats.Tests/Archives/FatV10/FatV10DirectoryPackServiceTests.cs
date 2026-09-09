using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10DirectoryPackServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "DuniaToolkit.Tests", Guid.NewGuid().ToString("N"));

    public FatV10DirectoryPackServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task BuildPacksRelativeResourcePathsAndPreservesUntouchedPayload()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        const string replacedPath = "graphics\\test\\changed.xbt";
        const string untouchedPath = "graphics\\test\\untouched.xbt";
        ArchivePair source = CreatePair(
            (DuniaPathHash.Compute(replacedPath), new byte[] { 1, 2, 3 }),
            (DuniaPathHash.Compute(untouchedPath), new byte[] { 4, 5 }));
        string input = Path.Combine(directory, "input");
        string replacementPath = Path.Combine(input, "graphics", "test", "changed.xbt");
        Directory.CreateDirectory(Path.GetDirectoryName(replacementPath)!);
        await File.WriteAllBytesAsync(replacementPath, [9, 8, 7, 6], token);
        var output = new ArchivePair(
            Path.Combine(directory, "output.fat"), Path.Combine(directory, "output.dat"));

        FatV10DirectoryPackResult result = await FatV10DirectoryPackService.BuildAsync(
            source, output, input, directory, token);

        FatV10DirectoryPackItem item = Assert.Single(result.Items);
        Assert.Equal(replacedPath, item.ResourcePath);
        Assert.Equal(0, Assert.Single(item.EntryIndices));
        Assert.Equal(1, result.Build.Build.ReplacementCount);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, await ExtractAsync(output, 0, token));
        Assert.Equal(new byte[] { 4, 5 }, await ExtractAsync(output, 1, token));
    }

    [Fact]
    public async Task BuildRejectsUnknownResourceWithoutPublishingOutput()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreatePair((DuniaPathHash.Compute("known.bin"), new byte[] { 1 }));
        string input = Path.Combine(directory, "unknown-input");
        Directory.CreateDirectory(input);
        await File.WriteAllBytesAsync(Path.Combine(input, "unknown.bin"), [2], token);
        var output = new ArchivePair(
            Path.Combine(directory, "unknown.fat"), Path.Combine(directory, "unknown.dat"));

        await Assert.ThrowsAsync<InvalidDataException>(() => FatV10DirectoryPackService.BuildAsync(
            source, output, input, directory, token));

        Assert.False(File.Exists(output.FatPath));
        Assert.False(File.Exists(output.DatPath));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(params (ulong Hash, byte[] Payload)[] items)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize * items.Length +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), items.Length);
        using var dat = new MemoryStream();
        for (int i = 0; i < items.Length; i++)
        {
            (ulong hash, byte[] payload) = items[i];
            FatV10EntryWriter.Write(
                new(hash, payload.Length, dat.Position, payload.Length, FatV10CompressionScheme.None, false),
                fat.AsSpan(
                    FatV10IndexSummaryReader.HeaderSize + i * FatV10IndexSummaryReader.EntrySize,
                    FatV10IndexSummaryReader.EntrySize));
            dat.Write(payload);
        }

        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, dat.ToArray());
        return new(fatPath, datPath);
    }

    private static async Task<byte[]> ExtractAsync(ArchivePair pair, int entryIndex, CancellationToken token)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(pair.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
        }

        await using FileStream dat = File.OpenRead(pair.DatPath);
        await using var output = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(dat, index.Entries[entryIndex], output, token);
        return output.ToArray();
    }
}
