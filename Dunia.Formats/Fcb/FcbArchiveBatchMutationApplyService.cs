using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveBatchMutationApplyService
{
    public static async Task<FcbArchiveBatchMutationApplyResult> ApplyAsync(
        ArchivePair target,
        int entryIndex,
        ulong expectedResourceNameHash,
        string expectedSourcePayloadSha256,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveFieldMutation> mutations,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(mutations);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        expectedSourcePayloadSha256 = NormalizeSha256(
            expectedSourcePayloadSha256,
            nameof(expectedSourcePayloadSha256));

        FcbArchiveBatchMutationArtifact artifact = await
            FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                target,
                entryIndex,
                expectedResourceNameHash,
                schema,
                mutations,
                cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                artifact.Plan.SourcePayloadSha256,
                expectedSourcePayloadSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"Source payload SHA-256 mismatch: expected {expectedSourcePayloadSha256}, got {artifact.Plan.SourcePayloadSha256}."));
        }

        if (artifact.Plan.NoOp)
        {
            return new(target, null, artifact.Plan, true, true);
        }

        FcbArchiveBatchMutationDryRunResult dryRun = await
            FcbArchiveBatchMutationDryRunService.RunAsync(
                target,
                artifact,
                temporaryRoot,
                cancellationToken).ConfigureAwait(false);
        if (!dryRun.IsVerified)
        {
            throw new InvalidDataException("FCB batch mutation dry-run verification failed.");
        }

        bool semanticVerified = false;
        using var store = new ReplacementStagingStore(Path.GetFullPath(temporaryRoot));
        using var replacementInput = new MemoryStream(artifact.PlannedPayload, false);
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
                FcbArchiveBatchMutationArtifact locked = await
                    FcbArchiveBatchMutationPlanService.CreateArtifactAsync(
                        lockedSource,
                        entryIndex,
                        expectedResourceNameHash,
                        schema,
                        mutations,
                        token).ConfigureAwait(false);
                if (!string.Equals(
                        locked.Plan.SourcePayloadSha256,
                        artifact.Plan.SourcePayloadSha256,
                        StringComparison.Ordinal) ||
                    !locked.PlannedPayload.AsSpan().SequenceEqual(artifact.PlannedPayload))
                {
                    throw new InvalidDataException(
                        "Locked FCB batch mutation differs from the verified plan.");
                }
            },
            async (published, token) =>
            {
                await ValidatePublishedAsync(
                    published,
                    artifact.Plan,
                    schema,
                    artifact.PlannedPayload,
                    token).ConfigureAwait(false);
                semanticVerified = true;
            },
            cancellationToken).ConfigureAwait(false);

        return new(target, apply.Backup, artifact.Plan, false, semanticVerified);
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
        FcbArchiveBatchMutationPlanResult plan,
        FcbValueSchema schema,
        byte[] expectedPayload,
        CancellationToken cancellationToken)
    {
        FatV10Index index;
        using (FileStream fat = File.OpenRead(published.FatPath))
        {
            index = FatV10IndexReader.Read(fat, new FileInfo(published.DatPath).Length);
        }

        if ((uint)plan.EntryIndex >= (uint)index.Entries.Count ||
            index.Entries[plan.EntryIndex].NameHash != plan.ResourceNameHash)
        {
            throw new InvalidDataException("Published FCB resource identity is invalid.");
        }

        await using var payload = new MemoryStream(expectedPayload.Length);
        await using (FileStream data = File.OpenRead(published.DatPath))
        {
            await FatV10PayloadExtractor.ExtractAsync(
                data,
                index.Entries[plan.EntryIndex],
                payload,
                cancellationToken).ConfigureAwait(false);
        }

        if (!payload.GetBuffer().AsSpan(0, checked((int)payload.Length)).SequenceEqual(expectedPayload))
        {
            throw new InvalidDataException("Published FCB payload differs from the verified batch mutation.");
        }

        payload.Position = 0;
        FcbDocument document = FcbReader.Read(payload);
        IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
        foreach (FcbArchivePlannedFieldMutation mutation in plan.Mutations)
        {
            if ((uint)mutation.NodeIndex >= (uint)nodes.Count ||
                (uint)mutation.FieldIndex >= (uint)nodes[mutation.NodeIndex].Fields.Count)
            {
                throw new InvalidDataException("A published FCB batch target is missing.");
            }

            FcbNode node = nodes[mutation.NodeIndex];
            FcbField field = node.Fields[mutation.FieldIndex];
            if (node.TypeHash != mutation.TypeHash ||
                field.NameHash != mutation.FieldHash ||
                field.IsReference ||
                !schema.TryResolve(node.TypeHash, field.NameHash, out FcbValueKind codec) ||
                codec != mutation.Codec ||
                !field.Data.Span.SequenceEqual(FcbValueEncoder.Encode(codec, mutation.RequestedValue)))
            {
                throw new InvalidDataException("Published FCB batch semantic validation failed.");
            }
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
