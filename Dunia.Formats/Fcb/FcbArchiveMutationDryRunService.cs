using System.Buffers;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveMutationDryRunService
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FcbArchiveMutationDryRunResult> RunAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        int nodeIndex,
        int fieldIndex,
        uint expectedTypeHash,
        uint expectedFieldHash,
        string value,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        long sourceDataLength = new FileInfo(source.DatPath).Length;
        FatV10Index sourceIndex;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            sourceIndex = FatV10IndexReader.Read(fat, sourceDataLength);
        }

        if ((uint)entryIndex >= (uint)sourceIndex.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex), "Archive entry index is out of range.");
        }

        FatV10Entry sourceEntry = sourceIndex.Entries[entryIndex];
        if (sourceEntry.NameHash != expectedResourceNameHash)
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"Resource hash mismatch: expected {expectedResourceNameHash:X16}, got {sourceEntry.NameHash:X16}."));
        }

        byte[] sourcePayload;
        await using (FileStream data = File.OpenRead(source.DatPath))
        await using (var payload = new MemoryStream(checked((int)sourceEntry.UncompressedSize)))
        {
            await FatV10PayloadExtractor.ExtractAsync(data, sourceEntry, payload, cancellationToken)
                .ConfigureAwait(false);
            sourcePayload = payload.ToArray();
        }

        using var fcbInput = new MemoryStream(sourcePayload, false);
        FcbDocument document = FcbReader.Read(fcbInput);
        IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
        if ((uint)nodeIndex >= (uint)nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeIndex), "FCB node index is out of range.");
        }

        FcbNode node = nodes[nodeIndex];
        if ((uint)fieldIndex >= (uint)node.Fields.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldIndex), "FCB field index is out of range.");
        }

        FcbField field = node.Fields[fieldIndex];
        if (node.TypeHash != expectedTypeHash || field.NameHash != expectedFieldHash)
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"FCB identity mismatch: actual={node.TypeHash:X8}:{field.NameHash:X8}."));
        }

        if (!schema.TryResolve(node.TypeHash, field.NameHash, out FcbValueKind codec))
        {
            throw new InvalidDataException("Target field has no schema codec.");
        }

        FcbValueMutationResult mutation = FcbValueMutator.ReplaceInlineField(
            document,
            field,
            FcbValueEncoder.Encode(codec, value),
            schema);

        string root = Path.GetFullPath(temporaryRoot);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, $"fcb-dryrun-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionPath);
        try
        {
            var outputPair = new ArchivePair(
                Path.Combine(sessionPath, "verified.fat"),
                Path.Combine(sessionPath, "verified.dat"));
            FatV10ArchivePatchFileBuildResult build;
            using (var store = new ReplacementStagingStore(sessionPath))
            using (var replacementInput = new MemoryStream(mutation.Data.ToArray(), false))
            {
                StagedReplacement replacement = await store.StageAsync(
                    replacementInput,
                    $"entry-{entryIndex}.fcb",
                    cancellationToken).ConfigureAwait(false);
                build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                    source,
                    outputPair,
                    new Dictionary<int, StagedReplacement> { [entryIndex] = replacement },
                    store,
                    cancellationToken).ConfigureAwait(false);
            }

            FatV10Entry rebuiltEntry = build.Build.Index.Entries[entryIndex];
            byte[] rebuiltPayload;
            await using (FileStream rebuiltData = File.OpenRead(outputPair.DatPath))
            await using (var payload = new MemoryStream(mutation.Data.Length))
            {
                await FatV10PayloadExtractor.ExtractAsync(rebuiltData, rebuiltEntry, payload, cancellationToken)
                    .ConfigureAwait(false);
                rebuiltPayload = payload.ToArray();
            }

            bool payloadExact = mutation.Data.Span.SequenceEqual(rebuiltPayload);
            bool untouchedEntriesExact = EntriesExceptTargetMatch(
                sourceIndex.Entries,
                build.Build.Index.Entries,
                entryIndex);
            bool prefixExact = await PrefixMatchesAsync(
                source.DatPath,
                outputPair.DatPath,
                sourceDataLength,
                cancellationToken).ConfigureAwait(false);
            string sha256 = Convert.ToHexString(SHA256.HashData(mutation.Data.Span));
            var result = new FcbArchiveMutationDryRunResult(
                entryIndex,
                sourceEntry.NameHash,
                mutation.Codec,
                mutation.Data.Length,
                sha256,
                build.Build.Index.Entries.Count,
                payloadExact,
                untouchedEntriesExact,
                prefixExact);
            if (!result.IsVerified)
            {
                throw new InvalidDataException("FCB archive mutation dry-run verification failed.");
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
                await source.ReadExactlyAsync(sourceBuffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                await rebuilt.ReadExactlyAsync(rebuiltBuffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
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
