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
        string expectedSourcePayloadSha256,
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
        expectedSourcePayloadSha256 = NormalizeSha256(
            expectedSourcePayloadSha256,
            nameof(expectedSourcePayloadSha256));

        FcbArchiveMutationPlanResult plan = await FcbArchiveMutationPlanService.CreateAsync(
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
        if (!string.Equals(
                plan.SourcePayloadSha256,
                expectedSourcePayloadSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"Source payload SHA-256 mismatch: expected {expectedSourcePayloadSha256}, got {plan.SourcePayloadSha256}."));
        }

        if (plan.NoOp)
        {
            return new(
                target,
                null,
                entryIndex,
                expectedResourceNameHash,
                plan.Codec,
                plan.SourcePayloadSha256,
                plan.SourcePayloadLength,
                plan.SourcePayloadSha256,
                plan.ArchiveEntryCount,
                true,
                true);
        }

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
        if (!string.Equals(mutationHash, dryRun.PayloadSha256, StringComparison.Ordinal) ||
            !string.Equals(mutationHash, plan.PlannedPayloadSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Repeated FCB mutation differs from the planned or dry-run payload.");
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
            async (lockedSource, token) =>
            {
                FcbArchiveMutationPlanResult lockedPlan = await FcbArchiveMutationPlanService.CreateAsync(
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
                if (!string.Equals(
                        lockedPlan.SourcePayloadSha256,
                        plan.SourcePayloadSha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        lockedPlan.PlannedPayloadSha256,
                        mutationHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Locked FCB mutation differs from the planned and verified payload.");
                }
            },
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
            plan.SourcePayloadSha256,
            mutation.Length,
            mutationHash,
            apply.Build.Index.Entries.Count,
            false,
            semanticVerified);
    }

    private static string NormalizeSha256(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Length != SHA256.HashSizeInBytes * 2 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal digits.", paramName);
        }

        return value.ToUpperInvariant();
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
