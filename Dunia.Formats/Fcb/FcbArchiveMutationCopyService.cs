using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveMutationCopyService
{
    public static async Task<FcbArchiveMutationCopyResult> CreateAsync(
        ArchivePair source,
        ArchivePair destination,
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
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        ValidateDestination(source, destination);

        FcbArchiveMutationDryRunResult dryRun = await FcbArchiveMutationDryRunService.RunAsync(
            source,
            entryIndex,
            expectedResourceNameHash,
            schema,
            nodeIndex,
            fieldIndex,
            expectedTypeHash,
            expectedFieldHash,
            value,
            temporaryRoot,
            cancellationToken).ConfigureAwait(false);

        byte[] mutation = await CreateMutationAsync(
            source,
            entryIndex,
            expectedResourceNameHash,
            schema,
            nodeIndex,
            fieldIndex,
            expectedTypeHash,
            expectedFieldHash,
            value,
            cancellationToken).ConfigureAwait(false);
        string mutationHash = Convert.ToHexString(SHA256.HashData(mutation));
        if (!string.Equals(mutationHash, dryRun.PayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Repeated FCB mutation differs from the verified dry-run payload.");
        }

        bool published = false;
        try
        {
            string stagingRoot = Path.GetFullPath(temporaryRoot);
            using var store = new ReplacementStagingStore(stagingRoot);
            using var replacementInput = new MemoryStream(mutation, false);
            StagedReplacement replacement = await store.StageAsync(
                replacementInput,
                $"entry-{entryIndex}.fcb",
                cancellationToken).ConfigureAwait(false);
            FatV10ArchivePatchFileBuildResult build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                source,
                destination,
                new Dictionary<int, StagedReplacement> { [entryIndex] = replacement },
                store,
                async (lockedSource, token) =>
                {
                    byte[] lockedMutation = await CreateMutationAsync(
                        lockedSource,
                        entryIndex,
                        expectedResourceNameHash,
                        schema,
                        nodeIndex,
                        fieldIndex,
                        expectedTypeHash,
                        expectedFieldHash,
                        value,
                        token).ConfigureAwait(false);
                    if (!lockedMutation.AsSpan().SequenceEqual(mutation))
                    {
                        throw new InvalidDataException(
                            "Locked FCB mutation differs from the verified copy payload.");
                    }
                },
                cancellationToken).ConfigureAwait(false);
            published = true;

            FatV10Entry outputEntry = build.Build.Index.Entries[entryIndex];
            byte[] outputPayload;
            await using (FileStream data = File.OpenRead(destination.DatPath))
            await using (var payload = new MemoryStream(mutation.Length))
            {
                await FatV10PayloadExtractor.ExtractAsync(data, outputEntry, payload, cancellationToken)
                    .ConfigureAwait(false);
                outputPayload = payload.ToArray();
            }

            bool payloadExact = mutation.AsSpan().SequenceEqual(outputPayload);
            if (!payloadExact)
            {
                throw new InvalidDataException("Published archive copy contains an unexpected FCB payload.");
            }

            return new(
                destination,
                entryIndex,
                expectedResourceNameHash,
                dryRun.Codec,
                mutation.Length,
                mutationHash,
                build.Build.Index.Entries.Count,
                true);
        }
        catch
        {
            if (published)
            {
                File.Delete(destination.FatPath);
                File.Delete(destination.DatPath);
            }

            throw;
        }
    }

    private static void ValidateDestination(ArchivePair source, ArchivePair destination)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string[] sourcePaths = [source.FatPath, source.DatPath];
        string[] destinationPaths = [destination.FatPath, destination.DatPath];
        if (destinationPaths.Any(destinationPath =>
                sourcePaths.Any(sourcePath => string.Equals(sourcePath, destinationPath, comparison))))
        {
            throw new ArgumentException("Source and destination archive paths must be different.");
        }

        if (destinationPaths.Any(File.Exists))
        {
            throw new IOException("Destination FAT/DAT files must not already exist.");
        }

        if (destinationPaths.Any(path => !Directory.Exists(Path.GetDirectoryName(path))))
        {
            throw new DirectoryNotFoundException("Destination FAT/DAT directory does not exist.");
        }
    }

    internal static async Task<byte[]> CreateMutationAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        int nodeIndex,
        int fieldIndex,
        uint expectedTypeHash,
        uint expectedFieldHash,
        string value,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(source.DatPath).Length);
        }

        if ((uint)entryIndex >= (uint)index.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex), "Archive entry index is out of range.");
        }

        FatV10Entry entry = index.Entries[entryIndex];
        if (entry.NameHash != expectedResourceNameHash)
        {
            throw new InvalidDataException("Resource identity changed after dry-run.");
        }

        await using var data = File.OpenRead(source.DatPath);
        await using var payload = new MemoryStream(checked((int)entry.UncompressedSize));
        await FatV10PayloadExtractor.ExtractAsync(data, entry, payload, cancellationToken).ConfigureAwait(false);
        payload.Position = 0;
        FcbDocument document = FcbReader.Read(payload);
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
        if (node.TypeHash != expectedTypeHash || field.NameHash != expectedFieldHash
            || !schema.TryResolve(node.TypeHash, field.NameHash, out FcbValueKind codec))
        {
            throw new InvalidDataException("FCB target identity or schema changed after dry-run.");
        }

        return FcbValueMutator.ReplaceInlineField(
            document,
            field,
            FcbValueEncoder.Encode(codec, value),
            schema).Data.ToArray();
    }
}
