using System.Buffers;
using System.Security.Cryptography;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ReplacementVerifier
{
    private const int BufferSize = 1024 * 1024;

    public static async Task<FatV10ReplacementVerificationResult> VerifyAsync(
        ArchivePair source,
        int entryIndex,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        long sourceDatLength = new FileInfo(source.DatPath).Length;
        FatV10Index sourceIndex;
        using (FileStream fat = File.OpenRead(source.FatPath))
        {
            sourceIndex = FatV10IndexReader.Read(fat, sourceDatLength);
        }

        if ((uint)entryIndex >= (uint)sourceIndex.Entries.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryIndex),
                entryIndex,
                $"Entry index must be between 0 and {sourceIndex.Entries.Count - 1}.");
        }

        string root = Path.GetFullPath(temporaryRoot);
        Directory.CreateDirectory(root);
        string sessionPath = Path.Combine(root, $"replacement-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionPath);

        try
        {
            string sourcePayloadPath = Path.Combine(sessionPath, "source-payload.bin");
            await ExtractAsync(source.DatPath, sourceIndex.Entries[entryIndex], sourcePayloadPath, cancellationToken)
                .ConfigureAwait(false);
            string sourcePayloadHash = await ComputeSha256Async(sourcePayloadPath, cancellationToken)
                .ConfigureAwait(false);

            var output = new ArchivePair(
                Path.Combine(sessionPath, "rebuilt.fat"),
                Path.Combine(sessionPath, "rebuilt.dat"));
            FatV10ArchivePatchFileBuildResult build;
            using (var store = new ReplacementStagingStore(sessionPath))
            {
                StagedReplacement replacement = await store.StageAsync(sourcePayloadPath, cancellationToken)
                    .ConfigureAwait(false);
                build = await FatV10ArchivePatchFileBuilder.BuildAsync(
                    source,
                    output,
                    new Dictionary<int, StagedReplacement> { [entryIndex] = replacement },
                    store,
                    cancellationToken).ConfigureAwait(false);
            }

            FatV10Entry rebuiltEntry = build.Build.Index.Entries[entryIndex];
            string rebuiltPayloadPath = Path.Combine(sessionPath, "rebuilt-payload.bin");
            await ExtractAsync(output.DatPath, rebuiltEntry, rebuiltPayloadPath, cancellationToken)
                .ConfigureAwait(false);
            string rebuiltPayloadHash = await ComputeSha256Async(rebuiltPayloadPath, cancellationToken)
                .ConfigureAwait(false);
            bool unchangedEntriesExact = EntriesExceptTargetMatch(
                sourceIndex.Entries,
                build.Build.Index.Entries,
                entryIndex);
            bool originalPrefixExact = await PrefixMatchesAsync(
                source.DatPath,
                output.DatPath,
                sourceDatLength,
                cancellationToken).ConfigureAwait(false);
            FatV10Entry sourceEntry = sourceIndex.Entries[entryIndex];

            return new(
                entryIndex,
                sourceEntry.NameHash,
                sourcePayloadHash,
                rebuiltPayloadHash,
                unchangedEntriesExact,
                originalPrefixExact,
                sourceEntry.CompressionScheme,
                rebuiltEntry.Offset);
        }
        finally
        {
            if (Directory.Exists(sessionPath))
            {
                Directory.Delete(sessionPath, true);
            }
        }
    }

    private static async Task ExtractAsync(
        string dataPath,
        FatV10Entry entry,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using FileStream data = File.OpenRead(dataPath);
        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await FatV10PayloadExtractor.ExtractAsync(data, entry, output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(true);
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

        return source[targetIndex].NameHash == rebuilt[targetIndex].NameHash &&
            source[targetIndex].UncompressedSize == rebuilt[targetIndex].UncompressedSize;
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

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
