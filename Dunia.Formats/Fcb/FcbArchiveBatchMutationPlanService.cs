using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveBatchMutationPlanService
{
    public static async Task<FcbArchiveBatchMutationPlanResult> CreateAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveFieldMutation> mutations,
        CancellationToken cancellationToken = default) =>
        (await CreateArtifactAsync(
            source,
            entryIndex,
            expectedResourceNameHash,
            schema,
            mutations,
            cancellationToken).ConfigureAwait(false)).Plan;

    internal static async Task<FcbArchiveBatchMutationArtifact> CreateArtifactAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        IReadOnlyList<FcbArchiveFieldMutation> mutations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count == 0)
        {
            throw new ArgumentException("At least one FCB mutation is required.", nameof(mutations));
        }

        cancellationToken.ThrowIfCancellationRequested();
        long sourceDataLength = new FileInfo(source.DatPath).Length;
        FatV10Index index;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            index = FatV10IndexReader.Read(fat, sourceDataLength);
        }

        if ((uint)entryIndex >= (uint)index.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex), "Archive entry index is out of range.");
        }

        FatV10Entry entry = index.Entries[entryIndex];
        if (entry.NameHash != expectedResourceNameHash)
        {
            throw new InvalidDataException(FormattableString.Invariant(
                $"Resource hash mismatch: expected {expectedResourceNameHash:X16}, got {entry.NameHash:X16}."));
        }

        byte[] sourcePayload;
        await using (FileStream data = File.OpenRead(source.DatPath))
        await using (var payload = new MemoryStream(checked((int)entry.UncompressedSize)))
        {
            await FatV10PayloadExtractor.ExtractAsync(data, entry, payload, cancellationToken)
                .ConfigureAwait(false);
            sourcePayload = payload.ToArray();
        }

        using var input = new MemoryStream(sourcePayload, false);
        FcbDocument document = FcbReader.Read(input);
        FcbValueSchemaCoverageReport coverage = FcbValueSchemaCoverageAnalyzer.Analyze(document, schema);
        if (!coverage.IsComplete)
        {
            throw new InvalidDataException("FCB value schema coverage is incomplete.");
        }

        IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
        var replacements = new List<FcbFieldReplacement>(mutations.Count);
        var planned = new List<FcbArchivePlannedFieldMutation>(mutations.Count);
        var selected = new HashSet<FcbField>(ReferenceEqualityComparer.Instance);
        foreach (FcbArchiveFieldMutation mutation in mutations)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            ArgumentNullException.ThrowIfNull(mutation.Value);
            if ((uint)mutation.NodeIndex >= (uint)nodes.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mutations),
                    $"FCB node index {mutation.NodeIndex} is out of range.");
            }

            FcbNode node = nodes[mutation.NodeIndex];
            if ((uint)mutation.FieldIndex >= (uint)node.Fields.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mutations),
                    $"FCB field index {mutation.NodeIndex}.{mutation.FieldIndex} is out of range.");
            }

            FcbField field = node.Fields[mutation.FieldIndex];
            if (node.TypeHash != mutation.ExpectedTypeHash || field.NameHash != mutation.ExpectedFieldHash)
            {
                throw new InvalidDataException(FormattableString.Invariant(
                    $"FCB identity mismatch at {mutation.NodeIndex}.{mutation.FieldIndex}: actual={node.TypeHash:X8}:{field.NameHash:X8}."));
            }

            if (!selected.Add(field))
            {
                throw new InvalidDataException(
                    $"FCB field {mutation.NodeIndex}.{mutation.FieldIndex} is targeted more than once.");
            }

            FcbTypedValueProjection current = FcbTypedValueProjector.Project(node.TypeHash, field, schema);
            if (current is not { Status: FcbTypedValueStatus.Resolved, Codec: not null, Value: not null })
            {
                throw new InvalidDataException(
                    $"Current FCB value at {mutation.NodeIndex}.{mutation.FieldIndex} cannot be projected.");
            }

            FcbValueKind codec = current.Codec.Value;
            byte[] requestedData = FcbValueEncoder.Encode(codec, mutation.Value);
            var requestedField = new FcbField(field.NameHash, requestedData, -1, null);
            FcbTypedValueProjection requested = FcbTypedValueProjector.Project(
                node.TypeHash,
                requestedField,
                schema);
            if (requested is not { Status: FcbTypedValueStatus.Resolved, Value: not null })
            {
                throw new InvalidDataException(
                    $"Requested FCB value at {mutation.NodeIndex}.{mutation.FieldIndex} cannot be projected.");
            }

            replacements.Add(new(field, requestedData));
            planned.Add(new(
                mutation.NodeIndex,
                mutation.FieldIndex,
                node.TypeHash,
                field.NameHash,
                codec,
                current.Value,
                requested.Value,
                Convert.ToHexString(requestedData)));
        }

        FcbValueBatchMutationResult result = FcbValueMutator.ReplaceInlineFields(
            document,
            replacements,
            schema);
        byte[] plannedPayload = result.Data.ToArray();
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(sourcePayload));
        string plannedSha256 = Convert.ToHexString(SHA256.HashData(plannedPayload));
        var plan = new FcbArchiveBatchMutationPlanResult(
            entryIndex,
            entry.NameHash,
            index.Entries.Count,
            planned.AsReadOnly(),
            coverage.FieldCount,
            coverage.ResolvedCount,
            sourcePayload.Length,
            sourceSha256,
            plannedPayload.Length,
            plannedSha256,
            sourcePayload.AsSpan().SequenceEqual(plannedPayload));
        return new(index, entry, sourceDataLength, sourcePayload, plannedPayload, plan);
    }
}
