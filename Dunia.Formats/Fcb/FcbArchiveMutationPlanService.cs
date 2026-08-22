using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveMutationPlanService
{
    public static async Task<FcbArchiveMutationPlanResult> CreateAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        int nodeIndex,
        int fieldIndex,
        uint expectedTypeHash,
        uint expectedFieldHash,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

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

        FcbValueSchemaCoverageReport coverage = FcbValueSchemaCoverageAnalyzer.Analyze(document, schema);
        if (!coverage.IsComplete)
        {
            throw new InvalidDataException("FCB value schema coverage is incomplete.");
        }

        FcbTypedValueProjection current = FcbTypedValueProjector.Project(node.TypeHash, field, schema);
        if (current is not { Status: FcbTypedValueStatus.Resolved, Codec: not null, Value: not null })
        {
            throw new InvalidDataException("Current FCB value cannot be projected using the schema.");
        }

        FcbValueKind codec = current.Codec.Value;
        byte[] requestedData = FcbValueEncoder.Encode(codec, value);
        var requestedField = new FcbField(field.NameHash, requestedData, -1, null);
        FcbTypedValueProjection requested = FcbTypedValueProjector.Project(node.TypeHash, requestedField, schema);
        if (requested is not { Status: FcbTypedValueStatus.Resolved, Value: not null })
        {
            throw new InvalidDataException("Requested FCB value cannot be projected using the schema.");
        }

        FcbValueMutationResult mutation = FcbValueMutator.ReplaceInlineField(
            document,
            field,
            requestedData,
            schema);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(sourcePayload));
        string plannedSha256 = Convert.ToHexString(SHA256.HashData(mutation.Data.Span));
        bool noOp = sourcePayload.AsSpan().SequenceEqual(mutation.Data.Span);

        return new(
            entryIndex,
            entry.NameHash,
            nodeIndex,
            fieldIndex,
            node.TypeHash,
            field.NameHash,
            codec,
            current.Value,
            requested.Value,
            Convert.ToHexString(requestedData),
            coverage.FieldCount,
            coverage.ResolvedCount,
            sourcePayload.Length,
            sourceSha256,
            mutation.Data.Length,
            plannedSha256,
            noOp);
    }
}
