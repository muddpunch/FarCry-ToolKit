using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveMutationApplyService
{
    public static async Task<FcbArchiveMutationApplyResult> ApplyAsync(
        ArchivePair target,
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
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);

        FcbArchiveMutationDryRunResult dryRun = await FcbArchiveMutationDryRunService.RunAsync(
            target,
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

        byte[] mutation = await FcbArchiveMutationCopyService.CreateMutationAsync(
            target,
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

        bool semanticVerified = false;
        using var store = new ReplacementStagingStore(Path.GetFullPath(temporaryRoot));
        using var replacementInput = new MemoryStream(mutation, false);
        StagedReplacement replacement = await store.StageAsync(
            replacementInput,
            $"entry-{entryIndex}.fcb",
            cancellationToken).ConfigureAwait(false);
        FatV10ArchivePatchApplyResult apply = await FatV10ArchivePatchApplyService.ApplyAsync(
            target,
            new Dictionary<int, StagedReplacement> { [entryIndex] = replacement },
            store,
            async (published, token) =>
            {
                await ValidatePublishedAsync(
                    published,
                    entryIndex,
                    expectedResourceNameHash,
                    schema,
                    nodeIndex,
                    fieldIndex,
                    expectedTypeHash,
                    expectedFieldHash,
                    dryRun.Codec,
                    value,
                    mutation,
                    token).ConfigureAwait(false);
                semanticVerified = true;
            },
            cancellationToken).ConfigureAwait(false);

        return new(
            target,
            apply.Backup,
            entryIndex,
            expectedResourceNameHash,
            dryRun.Codec,
            mutation.Length,
            mutationHash,
            apply.Build.Index.Entries.Count,
            semanticVerified);
    }

    private static async Task ValidatePublishedAsync(
        ArchivePair published,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        int nodeIndex,
        int fieldIndex,
        uint expectedTypeHash,
        uint expectedFieldHash,
        FcbValueKind expectedCodec,
        string value,
        byte[] expectedPayload,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(published.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(published.DatPath).Length);
        }

        if ((uint)entryIndex >= (uint)index.Entries.Count ||
            index.Entries[entryIndex].NameHash != expectedResourceNameHash)
        {
            throw new InvalidDataException("Published FCB resource identity is invalid.");
        }

        await using var payload = new MemoryStream(expectedPayload.Length);
        await using (FileStream data = File.OpenRead(published.DatPath))
        {
            await FatV10PayloadExtractor.ExtractAsync(
                data,
                index.Entries[entryIndex],
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        if (!payload.GetBuffer().AsSpan(0, checked((int)payload.Length)).SequenceEqual(expectedPayload))
        {
            throw new InvalidDataException("Published FCB payload differs from the verified mutation.");
        }

        payload.Position = 0;
        FcbDocument document = FcbReader.Read(payload);
        IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
        if ((uint)nodeIndex >= (uint)nodes.Count || (uint)fieldIndex >= (uint)nodes[nodeIndex].Fields.Count)
        {
            throw new InvalidDataException("Published FCB target is missing.");
        }

        FcbNode node = nodes[nodeIndex];
        FcbField field = node.Fields[fieldIndex];
        if (node.TypeHash != expectedTypeHash || field.NameHash != expectedFieldHash || field.IsReference ||
            !schema.TryResolve(node.TypeHash, field.NameHash, out FcbValueKind codec) || codec != expectedCodec ||
            !field.Data.Span.SequenceEqual(FcbValueEncoder.Encode(codec, value)))
        {
            throw new InvalidDataException("Published FCB semantic validation failed.");
        }

        if (!FcbValueSchemaCoverageAnalyzer.Analyze(document, schema).IsComplete)
        {
            throw new InvalidDataException("Published FCB schema coverage is incomplete.");
        }

        payload.Position = 0;
        if (!FcbRoundTripVerifier.Verify(payload).IsByteExact)
        {
            throw new InvalidDataException("Published FCB failed byte-exact round-trip verification.");
        }
    }
}
