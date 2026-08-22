using System.Security.Cryptography;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10PublishedReplacementValidator
{
    public static async Task ValidateAsync(
        ArchivePair published,
        IReadOnlyDictionary<int, StagedReplacement> replacements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(replacements);
        cancellationToken.ThrowIfCancellationRequested();

        long dataLength = new FileInfo(published.DatPath).Length;
        FatV10Index index;
        using (FileStream fat = File.OpenRead(published.FatPath))
        {
            index = FatV10IndexReader.Read(fat, dataLength);
        }

        await using FileStream data = File.OpenRead(published.DatPath);
        foreach ((int entryIndex, StagedReplacement replacement) in replacements.OrderBy(item => item.Key))
        {
            if ((uint)entryIndex >= (uint)index.Entries.Count)
            {
                throw new InvalidDataException($"Published replacement entry {entryIndex} is missing.");
            }

            ArgumentNullException.ThrowIfNull(replacement);
            FatV10Entry entry = index.Entries[entryIndex];
            if (entry.UncompressedSize != replacement.Length)
            {
                throw new InvalidDataException($"Published replacement entry {entryIndex} has an unexpected length.");
            }

            using SHA256 hash = SHA256.Create();
            await using (var output = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, true))
            {
                await FatV10PayloadExtractor.ExtractAsync(data, entry, output, cancellationToken)
                    .ConfigureAwait(false);
                output.FlushFinalBlock();
            }

            string actualHash = Convert.ToHexString(hash.Hash!);
            if (!string.Equals(actualHash, replacement.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Published replacement entry {entryIndex} failed SHA-256 validation.");
            }
        }
    }
}
