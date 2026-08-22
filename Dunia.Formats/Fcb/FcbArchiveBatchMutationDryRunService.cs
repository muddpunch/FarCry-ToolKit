using System.Buffers;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveBatchMutationDryRunService
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FcbArchiveBatchMutationDryRunResult> RunAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveFieldMutation> mutations,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        FcbArchiveBatchMutationArtifact artifact = await
            FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                source,
                entryIndex,
                expectedResourceNameHash,
                schema,
                mutations,
                cancellationToken).ConfigureAwait(false);
        return await RunAsync(source, artifact, temporaryRoot, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<FcbArchiveBatchMutationDryRunResult> RunAsync(
        ArchivePair source,
        FcbArchiveBatchMutationArtifact artifact,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(temporaryRoot);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, $"fcb-batch-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionPath);
        try
        {
            var outputPair = new ArchivePair(
                Path.Combine(sessionPath, "verified.fat"),
                Path.Combine(sessionPath, "verified.dat"));
            FatV10ArchivePatchFileBuildResult build;
            using (var store = new ReplacementStagingStore(sessionPath))
            using (var replacementInput = new MemoryStream(artifact.PlannedPayload, false))
            {
                StagedReplacement replacement = await store.StageAsync(
                    replacementInput,
                    $"entry-{artifact.Plan.EntryIndex}.fcb",
                    cancellationToken).ConfigureAwait(false);
                build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                    source,
                    outputPair,
                    new Dictionary<int, StagedReplacement> { [artifact.Plan.EntryIndex] = replacement },
                    store,
                    cancellationToken).ConfigureAwait(false);
            }

            FatV10Entry rebuiltEntry = build.Build.Index.Entries[artifact.Plan.EntryIndex];
            byte[] rebuiltPayload;
            await using (FileStream rebuiltData = File.OpenRead(outputPair.DatPath))
            await using (var payload = new MemoryStream(artifact.PlannedPayload.Length))
            {
                await FatV10PayloadExtractor.ExtractAsync(
                    rebuiltData,
                    rebuiltEntry,
                    payload,
                    cancellationToken).ConfigureAwait(false);
                rebuiltPayload = payload.ToArray();
            }

            var result = new FcbArchiveBatchMutationDryRunResult(
                artifact.Plan,
                artifact.PlannedPayload.AsSpan().SequenceEqual(rebuiltPayload),
                EntriesExceptTargetMatch(
                    artifact.SourceIndex.Entries,
                    build.Build.Index.Entries,
                    artifact.Plan.EntryIndex),
                await PrefixMatchesAsync(
                    source.DatPath,
                    outputPair.DatPath,
                    artifact.SourceDataLength,
                    cancellationToken).ConfigureAwait(false));
            if (!result.IsVerified)
            {
                throw new InvalidDataException("FCB batch mutation dry-run verification failed.");
            }

            return result;
        }
        finally
        {
            if (Directory.Exists(sessionPath))
            {
                Directory.Delete(sessionPath, true);
            }
        }
    }

    private static bool EntriesExceptTargetMatch(
        IReadOnlyList<FatV10Entry> source,
        IReadOnlyList<FatV10Entry> rebuilt,
        int targetIndex)
    {
        if (source.Count != rebuilt.Count)
        {
            return false;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (i != targetIndex && source[i] != rebuilt[i])
            {
                return false;
            }
        }

        return source[targetIndex].NameHash == rebuilt[targetIndex].NameHash;
    }

    private static async Task<bool> PrefixMatchesAsync(
        string sourcePath,
        string rebuiltPath,
        long length,
        CancellationToken cancellationToken)
    {
        byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] rebuiltBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using FileStream source = File.OpenRead(sourcePath);
            await using FileStream rebuilt = File.OpenRead(rebuiltPath);
            long remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(BufferSize, remaining);
                await source.ReadExactlyAsync(sourceBuffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                await rebuilt.ReadExactlyAsync(rebuiltBuffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                if (!sourceBuffer.AsSpan(0, count).SequenceEqual(rebuiltBuffer.AsSpan(0, count)))
                {
                    return false;
                }

                remaining -= count;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer, true);
            ArrayPool<byte>.Shared.Return(rebuiltBuffer, true);
        }
    }
}
