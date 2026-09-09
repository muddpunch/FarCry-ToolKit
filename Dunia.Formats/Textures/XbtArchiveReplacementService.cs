using System.Buffers.Binary;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Textures;

public static class XbtArchiveReplacementService
{
    public static async Task<XbtArchiveReplacementPlan> PlanAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateReplacementStream(replacementXbt);
        FatV10Index index = ReadIndex(source);
        FatV10Entry entry = GetEntry(index, entryIndex, expectedResourceNameHash);

        using var sourcePayload = new MemoryStream(entry.UncompressedSize);
        await using (FileStream data = File.OpenRead(source.DatPath))
        {
            await FatV10PayloadExtractor.ExtractAsync(data, entry, sourcePayload, cancellationToken).ConfigureAwait(false);
        }

        sourcePayload.Position = 0;
        await ValidateXbtAsync(sourcePayload, cancellationToken).ConfigureAwait(false);
        sourcePayload.Position = 0;
        string sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(sourcePayload, cancellationToken).ConfigureAwait(false));

        long replacementStart = replacementXbt.Position;
        await ValidateXbtAsync(replacementXbt, cancellationToken).ConfigureAwait(false);
        replacementXbt.Position = replacementStart;
        string replacementHash = Convert.ToHexString(
            await SHA256.HashDataAsync(replacementXbt, cancellationToken).ConfigureAwait(false));
        long replacementLength = replacementXbt.Length - replacementStart;
        replacementXbt.Position = replacementStart;

        byte[] binding = new byte[checked(4 + 8 + 32 + 32 + 8)];
        BinaryPrimitives.WriteInt32LittleEndian(binding, entryIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(binding.AsSpan(4), expectedResourceNameHash);
        Convert.FromHexString(sourceHash).CopyTo(binding, 12);
        Convert.FromHexString(replacementHash).CopyTo(binding, 44);
        BinaryPrimitives.WriteInt64LittleEndian(binding.AsSpan(76), replacementLength);
        string planHash = Convert.ToHexString(SHA256.HashData(binding));
        return new(entryIndex, expectedResourceNameHash, sourceHash, replacementHash, replacementLength, planHash);
    }

    public static async Task<XbtArchiveReplacementDryRunResult> DryRunAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        XbtArchiveReplacementPlan plan = await PlanAsync(
            source, entryIndex, expectedResourceNameHash, replacementXbt, cancellationToken).ConfigureAwait(false);
        string root = Path.Combine(Path.GetFullPath(temporaryRoot), $"xbt-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var store = new ReplacementStagingStore(root);
            StagedReplacement staged = await store.StageAsync(
                replacementXbt, "replacement.xbt", cancellationToken).ConfigureAwait(false);
            var replacements = new Dictionary<int, StagedReplacement> { [entryIndex] = staged };
            var output = new ArchivePair(Path.Combine(root, "output.fat"), Path.Combine(root, "output.dat"));
            FatV10ArchivePatchFileBuildResult built = await FatV10ArchivePatchFileBuilder.BuildAsync(
                source,
                output,
                replacements,
                store,
                (pair, token) => ValidateSourcePayloadHashAsync(
                    pair, entryIndex, expectedResourceNameHash, plan.SourcePayloadSha256, token),
                cancellationToken).ConfigureAwait(false);
            await FatV10PublishedReplacementValidator.ValidateAsync(output, replacements, cancellationToken).ConfigureAwait(false);
            await ValidatePublishedXbtAsync(output, entryIndex, cancellationToken).ConfigureAwait(false);
            return new(plan, built.Build, true);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    public static async Task<FatV10ArchivePatchFileBuildResult> CopyAsync(
        ArchivePair source,
        ArchivePair destination,
        string expectedPlanSha256,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        XbtArchiveReplacementPlan plan = await PlanAsync(
            source, entryIndex, expectedResourceNameHash, replacementXbt, cancellationToken).ConfigureAwait(false);
        RequirePlan(expectedPlanSha256, plan.PlanSha256);
        using var store = new ReplacementStagingStore(stagingRoot);
        StagedReplacement staged = await store.StageAsync(replacementXbt, "replacement.xbt", cancellationToken).ConfigureAwait(false);
        var replacements = new Dictionary<int, StagedReplacement> { [entryIndex] = staged };
        FatV10ArchivePatchFileBuildResult result = await FatV10ArchivePatchFileBuilder.BuildAsync(
            source,
            destination,
            replacements,
            store,
            (pair, token) => ValidateSourcePayloadHashAsync(
                pair, entryIndex, expectedResourceNameHash, plan.SourcePayloadSha256, token),
            cancellationToken).ConfigureAwait(false);
        try
        {
            await FatV10PublishedReplacementValidator.ValidateAsync(destination, replacements, cancellationToken).ConfigureAwait(false);
            await ValidatePublishedXbtAsync(destination, entryIndex, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            File.Delete(destination.FatPath);
            File.Delete(destination.DatPath);
            throw;
        }
    }

    public static async Task<FatV10ArchivePatchApplyResult> ApplyAsync(
        ArchivePair target,
        string expectedPlanSha256,
        int entryIndex,
        ulong expectedResourceNameHash,
        Stream replacementXbt,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        XbtArchiveReplacementPlan plan = await PlanAsync(
            target, entryIndex, expectedResourceNameHash, replacementXbt, cancellationToken).ConfigureAwait(false);
        RequirePlan(expectedPlanSha256, plan.PlanSha256);
        using var store = new ReplacementStagingStore(stagingRoot);
        StagedReplacement staged = await store.StageAsync(replacementXbt, "replacement.xbt", cancellationToken).ConfigureAwait(false);
        var replacements = new Dictionary<int, StagedReplacement> { [entryIndex] = staged };
        return await FatV10ArchivePatchApplyService.ApplyAsync(
            target,
            replacements,
            store,
            (pair, token) => ValidateSourcePayloadHashAsync(
                pair, entryIndex, expectedResourceNameHash, plan.SourcePayloadSha256, token),
            (pair, token) => ValidatePublishedXbtAsync(pair, entryIndex, token),
            cancellationToken).ConfigureAwait(false);
    }

    private static FatV10Index ReadIndex(ArchivePair pair)
    {
        using FileStream fat = File.OpenRead(pair.FatPath);
        return FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length);
    }

    private static FatV10Entry GetEntry(FatV10Index index, int entryIndex, ulong expectedHash)
    {
        if ((uint)entryIndex >= (uint)index.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex));
        }

        FatV10Entry entry = index.Entries[entryIndex];
        if (entry.NameHash != expectedHash)
        {
            throw new InvalidDataException("Archive entry resource hash does not match the request.");
        }

        return entry;
    }

    private static async Task ValidatePublishedXbtAsync(ArchivePair pair, int entryIndex, CancellationToken cancellationToken)
    {
        FatV10Entry entry = ReadIndex(pair).Entries[entryIndex];
        using var xbt = new MemoryStream(entry.UncompressedSize);
        await using FileStream data = File.OpenRead(pair.DatPath);
        await FatV10PayloadExtractor.ExtractAsync(data, entry, xbt, cancellationToken).ConfigureAwait(false);
        xbt.Position = 0;
        await ValidateXbtAsync(xbt, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateSourcePayloadHashAsync(
        ArchivePair pair,
        int entryIndex,
        ulong expectedResourceNameHash,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        FatV10Entry entry = GetEntry(ReadIndex(pair), entryIndex, expectedResourceNameHash);
        using var payload = new MemoryStream(entry.UncompressedSize);
        await using FileStream data = File.OpenRead(pair.DatPath);
        await FatV10PayloadExtractor.ExtractAsync(data, entry, payload, cancellationToken).ConfigureAwait(false);
        payload.Position = 0;
        string actualSha256 = Convert.ToHexString(
            await SHA256.HashDataAsync(payload, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Source XBT changed after planning.");
        }
    }

    private static async Task ValidateXbtAsync(Stream xbt, CancellationToken cancellationToken)
    {
        long start = xbt.Position;
        try
        {
            using var png = new MemoryStream();
            await XbtPngExporter.ExportAsync(xbt, png, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            xbt.Position = start;
        }
    }

    private static void ValidateReplacementStream(Stream replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!replacement.CanRead || !replacement.CanSeek)
        {
            throw new ArgumentException("Replacement XBT stream must be readable and seekable.", nameof(replacement));
        }
    }

    private static void RequirePlan(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"XBT replacement plan mismatch. Expected {expected}, got {actual}.");
        }
    }
}
