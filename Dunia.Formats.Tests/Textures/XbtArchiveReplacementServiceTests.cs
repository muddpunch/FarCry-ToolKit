using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Textures;

namespace Dunia.Formats.Tests.Textures;

public sealed class XbtArchiveReplacementServiceTests : IDisposable
{
    private const ulong ResourceHash = 0x0123456789ABCDEF;
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "DuniaToolkit.Tests", Guid.NewGuid().ToString("N"));

    public XbtArchiveReplacementServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task PlanIsStableAndBoundToReplacementPayload()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreatePair(CreateXbt(0x11));
        byte[] replacement = CreateXbt(0x22);

        XbtArchiveReplacementPlan first = await XbtArchiveReplacementService.PlanAsync(
            source, 0, ResourceHash, new MemoryStream(replacement), token);
        XbtArchiveReplacementPlan second = await XbtArchiveReplacementService.PlanAsync(
            source, 0, ResourceHash, new MemoryStream(replacement), token);
        XbtArchiveReplacementPlan changed = await XbtArchiveReplacementService.PlanAsync(
            source, 0, ResourceHash, new MemoryStream(CreateXbt(0x33)), token);

        Assert.Equal(first, second);
        Assert.NotEqual(first.PlanSha256, changed.PlanSha256);
        Assert.Equal(64, first.PlanSha256.Length);
        Assert.Equal(replacement.Length, first.ReplacementLength);
    }

    [Fact]
    public async Task DryRunBuildsAndValidatesWithoutPublishingFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreatePair(CreateXbt(0x11));

        XbtArchiveReplacementDryRunResult result = await XbtArchiveReplacementService.DryRunAsync(
            source, 0, ResourceHash, new MemoryStream(CreateXbt(0x22)), directory, token);

        Assert.True(result.Verified);
        Assert.Equal(1, result.Build.ReplacementCount);
        Assert.Empty(Directory.EnumerateDirectories(directory, "xbt-dryrun-*"));
        Assert.False(File.Exists(source.FatPath + ".original"));
        Assert.False(File.Exists(source.DatPath + ".original"));
    }

    [Fact]
    public async Task CopyRequiresMatchingPlanAndPublishesReplacement()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreatePair(CreateXbt(0x11));
        byte[] replacement = CreateXbt(0x22);
        XbtArchiveReplacementPlan plan = await XbtArchiveReplacementService.PlanAsync(
            source, 0, ResourceHash, new MemoryStream(replacement), token);
        var destination = new ArchivePair(
            Path.Combine(directory, "copy.fat"), Path.Combine(directory, "copy.dat"));

        await Assert.ThrowsAsync<InvalidDataException>(() => XbtArchiveReplacementService.CopyAsync(
            source, destination, new string('0', 64), 0, ResourceHash,
            new MemoryStream(replacement), directory, token));
        Assert.False(File.Exists(destination.FatPath));
        Assert.False(File.Exists(destination.DatPath));

        await XbtArchiveReplacementService.CopyAsync(
            source, destination, plan.PlanSha256, 0, ResourceHash,
            new MemoryStream(replacement), directory, token);

        Assert.Equal(replacement, await ExtractAsync(destination, token));
    }

    [Fact]
    public async Task CopyRejectsSourceChangedAfterPlanningWithoutPublishingFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreatePair(CreateXbt(0x11));
        byte[] replacement = CreateXbt(0x22);
        XbtArchiveReplacementPlan plan = await XbtArchiveReplacementService.PlanAsync(
            source, 0, ResourceHash, new MemoryStream(replacement), token);
        await File.WriteAllBytesAsync(source.DatPath, CreateXbt(0x33), token);
        var destination = new ArchivePair(
            Path.Combine(directory, "changed-copy.fat"), Path.Combine(directory, "changed-copy.dat"));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            XbtArchiveReplacementService.CopyAsync(
                source, destination, plan.PlanSha256, 0, ResourceHash,
                new MemoryStream(replacement), directory, token));

        Assert.Contains("plan mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination.FatPath));
        Assert.False(File.Exists(destination.DatPath));
    }

    [Fact]
    public async Task ApplyCreatesBackupsAndPublishesReplacement()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] original = CreateXbt(0x11);
        byte[] replacement = CreateXbt(0x22);
        ArchivePair target = CreatePair(original);
        XbtArchiveReplacementPlan plan = await XbtArchiveReplacementService.PlanAsync(
            target, 0, ResourceHash, new MemoryStream(replacement), token);

        await XbtArchiveReplacementService.ApplyAsync(
            target, plan.PlanSha256, 0, ResourceHash,
            new MemoryStream(replacement), directory, token);

        Assert.Equal(replacement, await ExtractAsync(target, token));
        Assert.True(File.Exists(target.FatPath + ".original"));
        Assert.Equal(original, await File.ReadAllBytesAsync(target.DatPath + ".original", token));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(byte[] payload)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize + FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), 1);
        FatV10EntryWriter.Write(
            new(ResourceHash, payload.Length, 0, payload.Length, FatV10CompressionScheme.None, false),
            fat.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, payload);
        return new(fatPath, datPath);
    }

    private static async Task<byte[]> ExtractAsync(ArchivePair pair, CancellationToken token)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(pair.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
        }

        await using FileStream data = File.OpenRead(pair.DatPath);
        await using var output = new MemoryStream();
        await FatV10PayloadExtractor.ExtractAsync(data, index.Entries[0], output, token);
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
