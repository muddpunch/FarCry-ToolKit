using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;
using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtArchiveTransactionServiceTests : IDisposable
{
    private static readonly ulong[] ResourceHashes = [0x0123456789ABCDEF, 0xFEDCBA9876543210];
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "DuniaToolkit.Tests", Guid.NewGuid().ToString("N"));

    public XbtArchiveTransactionServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task PlanIsOrderIndependentAndApplyPublishesEveryReplacement()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair(CreateXbt(0x11), CreateXbt(0x22));
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement first = await store.StageAsync(
            new MemoryStream(CreateXbt(0x33)), "first.xbt", token);
        StagedReplacement second = await store.StageAsync(
            new MemoryStream(CreateXbt(0x44)), "second.xbt", token);
        XbtArchiveReplacementPlan firstPlan = await XbtArchiveReplacementService.PlanAsync(
            target, 0, ResourceHashes[0], new MemoryStream(CreateXbt(0x33)), token);
        XbtArchiveReplacementPlan secondPlan = await XbtArchiveReplacementService.PlanAsync(
            target, 1, ResourceHashes[1], new MemoryStream(CreateXbt(0x44)), token);
        XbtArchiveTransactionItem[] items =
        [
            new(1, ResourceHashes[1], secondPlan.SourcePayloadSha256, secondPlan.ReplacementPayloadSha256, second),
            new(0, ResourceHashes[0], firstPlan.SourcePayloadSha256, firstPlan.ReplacementPayloadSha256, first),
        ];

        XbtArchiveTransactionPlan plan = await XbtArchiveTransactionService.PlanAsync(
            target, items, store, token);
        XbtArchiveTransactionPlan reversed = await XbtArchiveTransactionService.PlanAsync(
            target, items.AsEnumerable().Reverse().ToArray(), store, token);

        Assert.Equal(plan.PlanSha256, reversed.PlanSha256);
        Assert.Equal([0, 1], plan.Replacements.Select(item => item.EntryIndex));

        await XbtArchiveTransactionService.ApplyAsync(
            target, plan.PlanSha256, items, store, token);

        Assert.Equal(CreateXbt(0x33), await ExtractAsync(target, 0, token));
        Assert.Equal(CreateXbt(0x44), await ExtractAsync(target, 1, token));
        Assert.True(File.Exists(target.FatPath + ".original"));
        Assert.True(File.Exists(target.DatPath + ".original"));
    }

    [Fact]
    public async Task ApplyRejectsChangedSourceAndPreservesArchive()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair(CreateXbt(0x11), CreateXbt(0x22));
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(
            new MemoryStream(CreateXbt(0x33)), "replacement.xbt", token);
        XbtArchiveReplacementPlan itemPlan = await XbtArchiveReplacementService.PlanAsync(
            target, 0, ResourceHashes[0], new MemoryStream(CreateXbt(0x33)), token);
        XbtArchiveTransactionItem[] items =
        [
            new(
                0,
                ResourceHashes[0],
                itemPlan.SourcePayloadSha256,
                itemPlan.ReplacementPayloadSha256,
                replacement),
        ];
        XbtArchiveTransactionPlan plan = await XbtArchiveTransactionService.PlanAsync(
            target, items, store, token);
        byte[] changed = CreateXbt(0x55);
        await using (FileStream data = File.Open(target.DatPath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            await data.WriteAsync(changed, token);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => XbtArchiveTransactionService.ApplyAsync(
            target, plan.PlanSha256, items, store, token));

        Assert.Equal(changed, await ExtractAsync(target, 0, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
    }

    [Fact]
    public async Task PlanRejectsDuplicateEntryIndexes()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair(CreateXbt(0x11), CreateXbt(0x22));
        using var store = new ReplacementStagingStore(directory);
        byte[] replacementBytes = CreateXbt(0x33);
        StagedReplacement replacement = await store.StageAsync(
            new MemoryStream(replacementBytes), "replacement.xbt", token);
        XbtArchiveReplacementPlan itemPlan = await XbtArchiveReplacementService.PlanAsync(
            target, 0, ResourceHashes[0], new MemoryStream(replacementBytes), token);
        var item = new XbtArchiveTransactionItem(
            0,
            ResourceHashes[0],
            itemPlan.SourcePayloadSha256,
            itemPlan.ReplacementPayloadSha256,
            replacement);

        await Assert.ThrowsAsync<ArgumentException>(() => XbtArchiveTransactionService.PlanAsync(
            target, [item, item], store, token));
    }

    [Fact]
    public async Task ApplyRejectsMismatchedTransactionPlanAndPreservesArchive()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] original = CreateXbt(0x11);
        ArchivePair target = CreatePair(original, CreateXbt(0x22));
        using var store = new ReplacementStagingStore(directory);
        byte[] replacementBytes = CreateXbt(0x33);
        StagedReplacement replacement = await store.StageAsync(
            new MemoryStream(replacementBytes), "replacement.xbt", token);
        XbtArchiveReplacementPlan itemPlan = await XbtArchiveReplacementService.PlanAsync(
            target, 0, ResourceHashes[0], new MemoryStream(replacementBytes), token);
        XbtArchiveTransactionItem[] items =
        [
            new(
                0,
                ResourceHashes[0],
                itemPlan.SourcePayloadSha256,
                itemPlan.ReplacementPayloadSha256,
                replacement),
        ];

        await Assert.ThrowsAsync<InvalidDataException>(() => XbtArchiveTransactionService.ApplyAsync(
            target, new string('0', 64), items, store, token));

        Assert.Equal(original, await ExtractAsync(target, 0, token));
        Assert.False(File.Exists(target.FatPath + ".original"));
        Assert.False(File.Exists(target.DatPath + ".original"));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(params byte[][] payloads)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize + payloads.Length * FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), payloads.Length);
        int dataOffset = 0;
        for (int index = 0; index < payloads.Length; index++)
        {
            FatV10EntryWriter.Write(
                new(ResourceHashes[index], payloads[index].Length, dataOffset, payloads[index].Length,
                    FatV10CompressionScheme.None, false),
                fat.AsSpan(
                    FatV10IndexSummaryReader.HeaderSize + index * FatV10IndexSummaryReader.EntrySize,
                    FatV10IndexSummaryReader.EntrySize));
            dataOffset += payloads[index].Length;
        }

        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, payloads.SelectMany(payload => payload).ToArray());
        return new(fatPath, datPath);
    }

    private static async Task<byte[]> ExtractAsync(ArchivePair pair, int entryIndex, CancellationToken token)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(pair.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
        }

        await using FileStream data = File.OpenRead(pair.DatPath);
        await using var output = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(data, index.Entries[entryIndex], output, token);
        return output.ToArray();
    }

    private static byte[] CreateXbt(byte color)
    {
        byte[] xbt = new byte[36 + 156];
        BinaryPrimitives.WriteUInt32LittleEndian(xbt, 0x00584254);
        BinaryPrimitives.WriteUInt32LittleEndian(xbt.AsSpan(4), 116);
        BinaryPrimitives.WriteUInt32LittleEndian(xbt.AsSpan(8), 36);
        Span<byte> dds = xbt.AsSpan(36);
        BinaryPrimitives.WriteUInt32LittleEndian(dds, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[4..], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[12..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[16..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[28..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[76..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[80..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[84..], 0x30315844);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[128..], 71);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[132..], 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[140..], 1);
        dds[148] = color;
        dds[149] = color;
        return xbt;
    }
}
