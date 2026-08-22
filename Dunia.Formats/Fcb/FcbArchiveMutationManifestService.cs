using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveMutationManifestService
{
    public static async Task<FcbArchiveMutationManifestResult> CreateAsync(
        ArchivePair source,
        int entryIndex,
        ulong expectedResourceNameHash,
        FcbValueSchema schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);
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
        FcbValueSchemaCoverageReport coverage = FcbValueSchemaCoverageAnalyzer.Analyze(document, schema);
        if (!coverage.IsComplete)
        {
            throw new InvalidDataException("FCB value schema coverage is incomplete.");
        }

        IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
        var manifest = new List<FcbArchiveMutationManifestEntry>();
        int referencedFieldCount = 0;
        for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            FcbNode node = nodes[nodeIndex];
            for (int fieldIndex = 0; fieldIndex < node.Fields.Count; fieldIndex++)
            {
                FcbField field = node.Fields[fieldIndex];
                if (field.IsReference)
                {
                    referencedFieldCount++;
                    continue;
                }

                FcbTypedValueProjection projection = FcbTypedValueProjector.Project(
                    node.TypeHash,
                    field,
                    schema);
                if (projection is not
                    {
                        Status: FcbTypedValueStatus.Resolved,
                        Codec: not null,
                        Value: not null,
                    })
                {
                    throw new InvalidDataException(
                        $"FCB field {nodeIndex}.{fieldIndex} cannot be represented in a mutation manifest.");
                }

                FcbValueKind codec = projection.Codec.Value;
                if (!field.Data.Span.SequenceEqual(FcbValueEncoder.Encode(codec, projection.Value)))
                {
                    throw new InvalidDataException(
                        $"FCB field {nodeIndex}.{fieldIndex} cannot be canonically re-encoded byte-exactly.");
                }

                manifest.Add(new(
                    nodeIndex,
                    fieldIndex,
                    node.TypeHash,
                    field.NameHash,
                    codec,
                    projection.Value));
            }
        }

        if (manifest.Count == 0)
        {
            throw new InvalidDataException("FCB contains no independently editable inline fields.");
        }

        return new(
            entryIndex,
            entry.NameHash,
            index.Entries.Count,
            coverage.FieldCount,
            coverage.ResolvedCount,
            sourcePayload.Length,
            Convert.ToHexString(SHA256.HashData(sourcePayload)),
            manifest.AsReadOnly(),
            referencedFieldCount);
    }
}
