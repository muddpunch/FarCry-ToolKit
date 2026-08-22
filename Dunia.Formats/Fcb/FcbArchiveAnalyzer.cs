using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveAnalyzer
{
    public static async Task<FcbArchiveAnalysisResult> AnalyzeAsync(
        Stream data,
        FatV10Index index,
        int maxPayloadSize = FcbArchiveScanner.DefaultMaxPayloadSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(index);
        if (!data.CanRead || !data.CanSeek)
        {
            throw new ArgumentException("DAT stream must be readable and seekable.", nameof(data));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxPayloadSize, FcbReader.HeaderSize);

        var resources = new List<FcbArchiveResource>();
        var typeHashes = new Dictionary<uint, int>();
        var fieldHashes = new Dictionary<uint, int>();
        int scanned = 0;
        int skipped = 0;

        for (int i = 0; i < index.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FatV10Entry entry = index.Entries[i];
            if (entry.UncompressedSize < FcbReader.HeaderSize || entry.UncompressedSize > maxPayloadSize)
            {
                skipped++;
                continue;
            }

            scanned++;
            using var payload = new MemoryStream(entry.UncompressedSize);
            await FatV10PayloadExtractor.ExtractAsync(data, entry, payload, cancellationToken)
                .ConfigureAwait(false);
            if (payload.Length < sizeof(uint)
                || BinaryPrimitives.ReadUInt32LittleEndian(payload.GetBuffer()) != FcbReader.Signature)
            {
                continue;
            }

            payload.Position = 0;
            FcbDocument document;
            try
            {
                document = FcbReader.Read(payload);
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or NotSupportedException)
            {
                throw new InvalidDataException($"FCB archive entry {i} could not be parsed.", ex);
            }

            IReadOnlyList<FcbNode> nodes = FcbGraph.GetUniqueNodes(document);
            foreach (FcbNode node in nodes)
            {
                Increment(typeHashes, node.TypeHash);
                foreach (FcbField field in node.Fields)
                {
                    Increment(fieldHashes, field.NameHash);
                }
            }

            resources.Add(new(
                i,
                entry.NameHash,
                entry.UncompressedSize,
                entry.CompressionScheme,
                document.UniqueNodeCount,
                document.FieldCount));
        }

        return new(
            resources.AsReadOnly(),
            new ReadOnlyDictionary<uint, int>(typeHashes),
            new ReadOnlyDictionary<uint, int>(fieldHashes),
            scanned,
            skipped);
    }

    private static void Increment(Dictionary<uint, int> occurrences, uint hash)
    {
        occurrences.TryGetValue(hash, out int count);
        occurrences[hash] = checked(count + 1);
    }
}
